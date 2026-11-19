namespace SpaceEngine

open Fable.Core
open Browser.Types

/// Der Sternenhintergrund wird einmalig auf der GPU erzeugt. Die Himmelskörper des
/// aktuellen Sektors entstehen beim Sektorwechsel in Bildschirmauflösung, damit Planetenrand
/// und Oberfläche scharf bleiben statt aus einem kleinen Atlas vergrößert zu werden.
type SkyBackdrop =
    { Program: WebGLProgram
      Texture: WebGLTexture
      mutable Bodies: WebGLTexture
      /// Sonnenrichtungen der sechs Ansichten für deren Shader.
      BodySuns: JS.Float32Array
      MaxBodyWidth: int
      /// Ansicht und Breite der aktuellen Textur; -1 = noch keine erzeugt.
      mutable BodyCell: int
      mutable BodyWidth: int
      SectorCells: int[]
      Vao: WebGLVertexArrayObject
      UView: WebGLUniformLocation
      UHdr: WebGLUniformLocation
      USector: WebGLUniformLocation
      USun: WebGLUniformLocation
      USunColor: WebGLUniformLocation
      ULens: WebGLUniformLocation }

[<RequireQualifiedAccess>]
module SkyBackdrop =
    let private atlasSectors =
        [| "outer-belt"; "slag-refinery"; "frontier-drift"; "kepler-yard"; "aether-relay"; "morgenlicht-bastion"
           "kaltstrom"; "wracksaum"; "schleierpass" |]

    let private vertex = """#version 300 es
precision highp float;
out vec2 vUv;
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
"""

    let private bakeHeader = """#version 300 es
precision highp float;
in vec2 vUv;
out vec4 outColor;
const float PI = 3.14159265359;
float hash(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}
float noise(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i+vec3(1,0,0)), f.x),
                   mix(hash(i+vec3(0,1,0)), hash(i+vec3(1,1,0)), f.x), f.y),
               mix(mix(hash(i+vec3(0,0,1)), hash(i+vec3(1,0,1)), f.x),
                   mix(hash(i+vec3(0,1,1)), hash(i+vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm(vec3 p) {
    float n = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) {
        n += noise(p) * a;
        p = p * 2.03 + vec3(9.2, 3.8, 5.1);
        a *= 0.5;
    }
    return n;
}
float sphere(vec3 rd, vec3 center, float radius) {
    float b = dot(rd, center);
    float h = b*b - dot(center, center) + radius*radius;
    if (h < 0.0 || b < 0.0) return 1e5;
    return b - sqrt(h);
}
vec3 stars(vec3 d, float scale, float threshold, float radius) {
    vec3 p = d * scale, cell = floor(p), f = fract(p) - 0.5;
    float seed = hash(cell);
    float star = (1.0-smoothstep(0.0, radius, length(f))) * step(threshold, seed);
    return mix(vec3(0.52,0.74,1.0), vec3(1.0,0.78,0.49), hash(cell+9.0)) * star;
}
"""
    let private galaxyFragment = bakeHeader + """
void main() {
    float lon = (vUv.x - 0.5) * 2.0 * PI;
    float lat = (vUv.y - 0.5) * PI;
    vec3 d = vec3(sin(lon)*cos(lat), sin(lat), -cos(lon)*cos(lat));

    float warp = fbm(d * 3.3 + vec3(1.0,4.0,7.0));
    float offset = (d.y + d.x * 0.38 - 0.12 + (warp - 0.5) * 0.34) * 3.4;
    float band = exp(-offset * offset);
    float cloud = fbm(d * 8.0 + warp * 2.5);
    float wisps = fbm(d * 22.0 + cloud * 3.0);
    // Ash rather than aurora: the band is dimmer, half drained of colour and cut by wider dust lanes.
    float dust = smoothstep(0.34, 0.62, fbm(d * 13.0 + vec3(3,7,1)));
    vec3 color = vec3(0.003, 0.005, 0.013);
    vec3 nebula = mix(vec3(0.088,0.036,0.108), vec3(0.030,0.118,0.138), smoothstep(-0.7,0.7,d.x));
    color += nebula * band * (0.12 + cloud * cloud * 1.25) * (0.44 + wisps);
    color *= 1.0 - dust * band * 0.93;
    color += vec3(0.05,0.075,0.105) * pow(wisps, 4.0) * band;
    color += stars(d, 480.0, 0.988, 0.36) * 0.55 + stars(d, 320.0, 0.985, 0.31) * 0.90 + stars(d, 190.0, 0.994, 0.27) * 1.55;

    outColor = vec4(sqrt(clamp(color * 0.25, 0.0, 1.0)), 1.0);
}
"""
    // Ein gemeinsamer Ausschnitt der vorderen Himmelshalbkugel. Außerhalb
    // dieses Fensters zeigt der Renderer nur das nahtlose 360°-Sternenfeld.
    let private windowShader = """
const vec2 SKY_MIN = vec2(-1.24, -0.72);
const vec2 SKY_SIZE = vec2(2.52, 1.70);
"""
    let private bodyFragment = bakeHeader + windowShader + """
uniform vec3 uSuns[9];
uniform int uCell;
uniform vec2 uJitter;
const vec3 SUN = vec3(0.8393, 0.4519, 0.3013);
vec4 kestrel(vec3 d) {
    vec3 color = vec3(0.0);
    float coverage = 0.0;
    // Ray/sphere planet and ray/plane rings share depth, including the far side
    // of the rings and the planet's shadow. They stay fixed in world space.
    vec3 center = normalize(vec3(0.57, 0.24, -1.0)) * 12.0;
    float radius = 2.12;
    float planetT = sphere(d, center, radius);
    float approach = length(cross(d, center));
    float halo = exp(-max(approach-radius,0.0)*22.0) * step(radius, approach) * step(0.0,dot(d,center));
    color += vec3(0.06,0.30,0.49) * halo * 0.7;
    if (planetT < 1e4) {
        coverage = 1.0;
        vec3 n = normalize(d * planetT - center);
        float turbulence = fbm(n * 8.0);
        float bands = sin(n.y * 39.0 + turbulence * 3.2 + n.x * 0.7);
        vec3 surface = mix(vec3(0.20,0.29,0.32),vec3(0.51,0.63,0.61),smoothstep(-1.0,1.0,bands));
        surface *= 0.85 + turbulence * 0.32 + sin(n.y*48.0+turbulence*6.0)*0.025;
        surface = mix(surface,vec3(0.62,0.45,0.29),smoothstep(0.57,0.76,turbulence)*0.8);
        float daylight = max(dot(n,SUN),0.0);
        float limb = pow(1.0-max(dot(n,-d),0.0),3.0);
        vec3 termTint = mix(vec3(1.0,0.68,0.44), vec3(1.0,0.88,0.73), smoothstep(0.0, 0.35, daylight));
        color = surface * (vec3(0.022,0.035,0.06) + termTint*daylight*0.95);
        color += vec3(0.09,0.42,0.65)*limb*(0.14+daylight*0.85);
    }
    vec3 ringN = normalize(vec3(0.18,0.79,0.42));
    float denom = dot(d,ringN);
    // Ring radius and its screen-space footprint are evaluated for every texel so
    // the derivative below is defined; the edge-on line is masked by the test.
    float t = dot(center,ringN)/(abs(denom) > 0.0001 ? denom : 0.0001);
    vec3 p = d*t-center;
    float r = length(p);
    float fine = fwidth(r*31.0);
    if (abs(denom) > 0.0001) {
        if (t > 0.0 && t < planetT && r > 2.65 && r < 4.55) {
            // Fine ringlets fade to their mean before they approach the texel
            // grid; the broad bands stay and carry the ring at any distance.
            float ringlets = sin(r*31.0+noise(p*9.0)*0.18) * (1.0-smoothstep(0.55,1.7,fine));
            float bands = noise(vec3(r*5.3,1.7,4.1))-0.5;
            float stripes = clamp(0.5+0.36*ringlets+0.75*bands,0.0,1.0);
            float density = smoothstep(2.65,2.82,r)*(1.0-smoothstep(4.25,4.55,r));
            // The gap never gets narrower than a few texels, so it stays a line.
            float gap = max(0.06,fine*0.09);
            density *= (0.30+stripes*0.52)*(0.05+smoothstep(0.25*gap,gap,abs(r-3.50))*0.95);
            float shadow = sphere(SUN,-p,radius) < 1e4 ? 0.12 : 1.0;
            vec3 ringColor = mix(vec3(0.37,0.39,0.37),vec3(0.67,0.59,0.45),stripes) * shadow;
            color = mix(color,ringColor,density);
            coverage = mix(coverage,1.0,density);
        }
    }

    vec3 moonCenter = normalize(vec3(-0.57,0.36,-1.0))*16.0;
    float moonT = sphere(d,moonCenter,0.52);
    if (moonT < 1e4) {
        coverage = 1.0;
        vec3 n = normalize(d*moonT-moonCenter);
        float rock = fbm(n*14.0);
        color = vec3(0.30,0.34,0.40)*(0.4+rock)*(0.035+max(dot(n,SUN),0.0));
    }
    return vec4(color,coverage);
}

// Alle folgenden Oberflächen rechnen nur beim Erzeugen der Sektoransicht.
vec4 world(vec3 d, vec3 center, float radius, vec3 sunDir, int kind) {
    float miss = length(cross(d,center)) - radius;
    vec3 air = kind==1 ? vec3(0.64,0.10,0.015) :
               kind==2 ? vec3(0.07,0.47,0.56) : vec3(0.22,0.47,0.74);
    if (kind==0) air=vec3(0.0);
    float t = sphere(d,center,radius);
    if (t>1e4) return vec4(air * exp(-max(miss,0.0)*24.0/radius)*0.42,0.0);
    vec3 n=normalize(d*t-center);
    float continents=fbm(n*3.7+vec3(8,1,4));
    float detail=fbm(n*19.0+2.8*continents);
    float daylight=dot(n,sunDir);
    float facing=clamp(dot(n,-d),0.0,1.0);
    float rim=pow(1.0-facing,3.0);
    vec3 surface, emission=vec3(0.0);
    if (kind==1) {
        // Hot spreading ridges cut through cold, mottled basalt plates.
        float fault=abs(fbm(n*5.2+continents*3.4)-0.49);
        float bed=exp(-fault*40.0);
        float lava=exp(-fault*190.0)*(0.35+0.65*smoothstep(0.24,0.61,detail));
        surface=mix(vec3(0.10,0.066,0.074),vec3(0.47,0.24,0.14),detail);
        emission=vec3(0.27,0.018,0.002)*bed+vec3(1.80,0.36,0.026)*lava;
    } else if (kind==2) {
        // Shallow continental shelves, green islands and broken spiral cloud bands.
        float land=smoothstep(0.491,0.525,continents+0.018*detail);
        vec3 sea=mix(vec3(0.018,0.09,0.18),vec3(0.05,0.58,0.58),smoothstep(0.36,0.50,continents));
        surface=mix(sea,mix(vec3(0.18,0.37,0.19),vec3(0.57,0.58,0.34),detail),land);
        float swirl=fbm(n*9.0+vec3(3.1*sin(n.y*5.0+continents*3.0),0,0));
        float cloud=smoothstep(0.50,0.66,swirl+0.06*sin(n.y*23.0+detail*8.0));
        surface=mix(surface,vec3(0.80,0.91,0.87),cloud*0.92);
        emission=vec3(0.50,0.79,0.85)*pow(max(dot(reflect(-sunDir,n),-d),0.0),30.0)*(1.0-land)*(1.0-cloud)*0.48;
    } else if (kind==3) {
        // Pale ice plateaux with dark branching fractures and wind-scoured strata.
        float rift=abs(fbm(n*3.4+vec3(detail*1.9,0.4,3.0))-0.48);
        float seam=1.0-smoothstep(0.003,0.025,rift);
        float strata=0.5+0.5*sin(19.0*n.y+continents*10.0+detail*3.0);
        surface=mix(vec3(0.34,0.52,0.63),vec3(0.89,0.95,0.94),0.42*strata+0.53*detail);
        surface=mix(surface,vec3(0.12,0.28,0.41),seam*0.82);
        emission=vec3(0.027,0.050,0.084)*(0.5+detail);
    } else {
        surface=mix(vec3(0.15,0.13,0.15),vec3(0.54,0.48,0.41),detail);
    }
    vec3 termLight=mix(vec3(1.0,0.62,0.40), vec3(0.99,0.91,0.82), smoothstep(0.0, 0.32, daylight));
    vec3 color=surface*(vec3(0.035,0.049,0.072)+max(daylight,0.0)*termLight);
    color+=air*rim*(0.12+0.82*smoothstep(-0.25,0.65,daylight));
    return vec4(color+emission,1.0);
}

vec4 pulsar(vec2 p) {
    // A thin tilted accretion disk with a compact white core and bipolar jets.
    vec2 q=p-vec2(-0.42,0.23);
    q=mat2(0.900,-0.436,0.436,0.900)*q;
    float r=length(q);
    float disk=length(vec2(q.x,q.y*5.3));
    float angle=atan(q.y*5.3,q.x);
    float ripples=0.5+0.5*sin(114.0*disk-angle*2.0+1.8*noise(vec3(q*38.0,7.0)));
    float ringOffset=(disk-0.164)/0.054;
    float ribbon=exp(-ringOffset*ringOffset)*(0.42+0.58*ripples);
    float axial=abs(q.y);
    float jetOffset=q.x/(0.005+axial*0.065);
    float jet=exp(-jetOffset*jetOffset)*exp(-axial*9.0);
    jet*=1.0-smoothstep(0.33,0.46,axial);
    vec3 color=vec3(0.32,0.16,0.68)*exp(-r*16.0);
    color+=vec3(0.65,0.30,1.10)*ribbon+vec3(0.22,0.73,1.65)*jet;
    color+=vec3(2.55,2.80,3.20)*exp(-r*r/0.00024);
    return vec4(color,clamp(ribbon*0.4+jet*0.43+exp(-r*r/0.00018),0.0,1.0));
}

vec4 eclipse(vec2 p) {
    vec2 q=p-vec2(0.43,0.19);
    float r=length(q), a=atan(q.y,q.x);
    float star=length(q-vec2(-0.027,0.017));
    float stream=0.55+0.45*noise(vec3(cos(a)*11.0,sin(a)*11.0,5.0));
    float corona=exp(-max(star-0.176,0.0)*(23.0-6.0*stream));
    vec3 radiance=vec3(1.14,0.43,0.09)*corona*stream;
    float photosphere=1.0-smoothstep(0.174,0.179,star);
    radiance+=vec3(2.8,1.75,0.63)*photosphere;
    float cover=1.0-smoothstep(0.186,0.190,r);
    vec3 night=vec3(0.017,0.012,0.022)*(0.48+noise(vec3(q*26.0,3.3)));
    night+=vec3(0.20,0.072,0.020)*smoothstep(0.156,0.189,r)*stream;
    return vec4(mix(radiance,night,cover),max(cover,photosphere));
}

// Kaltstrom: blasser Eisriese, dicht vor dem Fenster, mit schmalem Ring und dessen Schatten.
vec4 iceGiant(vec3 d, vec3 sun) {
    vec3 center=normalize(vec3(-0.50,0.06,-1.0))*9.0;
    float radius=3.05;
    vec3 ringN=normalize(vec3(0.10,0.93,0.34));
    vec3 color=vec3(0.0);
    float coverage=0.0;
    float approach=length(cross(d,center));
    color+=vec3(0.10,0.42,0.55)*exp(-max(approach-radius,0.0)*14.0)*step(radius,approach)*step(0.0,dot(d,center))*0.6;
    float planetT=sphere(d,center,radius);
    if (planetT<1e4) {
        coverage=1.0;
        vec3 n=normalize(d*planetT-center);
        float lat=dot(n,ringN);
        float turb=fbm(n*5.0+vec3(2.0,0.0,5.0));
        float bands=0.5+0.5*sin(lat*26.0+turb*2.6);
        vec3 surface=mix(vec3(0.30,0.55,0.66),vec3(0.78,0.90,0.92),bands*0.7+turb*0.25);
        surface=mix(surface,vec3(0.16,0.34,0.52),smoothstep(0.55,0.95,abs(lat)));
        float daylight=max(dot(n,sun),0.0);
        // Der Ring wirft ein dunkles Band auf die Tagseite.
        vec3 at=n*radius;
        float toSun=dot(sun,ringN);
        float shade=1.0;
        if (abs(toSun)>0.001) {
            float reach=-dot(at,ringN)/toSun;
            float r=length(at+sun*reach);
            if (reach>0.0) shade=1.0-0.62*smoothstep(3.55,3.75,r)*(1.0-smoothstep(5.0,5.25,r));
        }
        float limb=pow(1.0-max(dot(n,-d),0.0),3.0);
        color=surface*(vec3(0.020,0.035,0.055)+vec3(0.95,1.0,1.05)*daylight*shade);
        color+=vec3(0.12,0.50,0.66)*limb*(0.12+daylight*0.8);
    }
    float denom=dot(d,ringN);
    float t=dot(center,ringN)/(abs(denom)>0.0001 ? denom : 0.0001);
    vec3 q=d*t-center;
    float r=length(q);
    float fine=fwidth(r*40.0);
    if (abs(denom)>0.0001 && t>0.0 && t<planetT && r>3.55 && r<5.25) {
        float lanes=0.5+0.5*sin(r*40.0)*(1.0-smoothstep(0.6,1.8,fine));
        float density=smoothstep(3.55,3.70,r)*(1.0-smoothstep(5.0,5.25,r))*(0.25+0.55*lanes)*(0.4+0.6*noise(vec3(r*7.0,2.0,9.0)));
        float shadow=sphere(sun,-q,radius)<1e4 ? 0.10 : 1.0;
        vec3 ringColor=mix(vec3(0.55,0.72,0.80),vec3(0.90,0.96,0.98),lanes)*shadow*0.9;
        color=mix(color,ringColor,density);
        coverage=mix(coverage,1.0,density);
    }
    return vec4(color,coverage);
}

// Wracksaum: aufgebrochener Mond mit glühender Wunde, darum ein Bogen aus Trümmern.
vec4 brokenMoon(vec3 d, vec3 sun) {
    vec3 center=normalize(vec3(0.50,0.26,-1.0))*11.0;
    float radius=1.9;
    vec4 body=vec4(0.0);
    float moonT=sphere(d,center,radius);
    if (moonT<1e4) {
        vec3 n=normalize(d*moonT-center);
        float gap=dot(n,normalize(vec3(-0.55,0.45,0.70)));
        float crack=abs(fbm(n*4.0+vec3(3.0,1.0,7.0))-0.5);
        float rock=fbm(n*11.0);
        vec3 surface=mix(vec3(0.16,0.14,0.13),vec3(0.50,0.44,0.38),rock);
        float daylight=max(dot(n,sun),0.0);
        vec3 color=surface*(vec3(0.030,0.035,0.045)+vec3(1.0,0.86,0.70)*daylight);
        float wound=smoothstep(0.80,0.93,gap);
        float veins=exp(-crack*150.0)*smoothstep(0.50,0.90,gap);
        color=mix(color,vec3(0.05,0.02,0.015),wound*0.85);
        color+=vec3(1.6,0.42,0.06)*(veins*0.9+wound*exp(-crack*30.0)*0.8);
        body=vec4(color,1.0);
    }
    vec3 ringN=normalize(vec3(-0.30,0.82,0.48));
    float denom=dot(d,ringN);
    float t=dot(center,ringN)/(abs(denom)>0.0001 ? denom : 0.0001);
    vec3 q=d*t-center;
    float r=length(q);
    if (abs(denom)>0.0001 && t>0.0 && t<moonT && r>2.5 && r<4.6) {
        float clump=fbm(q*1.3);
        float chunks=smoothstep(0.80,0.86,noise(q*7.0)+0.35*clump-0.10);
        float band=smoothstep(2.5,3.0,r)*(1.0-smoothstep(3.7,4.6,r));
        float density=clamp(band*(clump*clump*0.22+chunks*0.9),0.0,1.0);
        vec3 debris=mix(vec3(0.20,0.16,0.13),vec3(0.62,0.52,0.40),noise(q*5.0))*0.55;
        body=vec4(mix(body.rgb,debris,density),mix(body.a,1.0,density));
    }
    return body;
}

// Schleierpass: leuchtender Nebelvorhang mit dunklen Staubbahnen, dahinter ein roter Zwerg.
vec4 veil(vec2 p) {
    vec2 q=p-vec2(-0.25,0.05);
    float warp=fbm(vec3(q*1.6,3.0));
    float fold=fbm(vec3(q.x*5.5+warp*2.4,q.y*1.1,8.0));
    float across=(q.x+0.25*(warp-0.5)+0.18*sin(q.y*2.2))*1.7;
    float sheet=exp(-across*across);
    float filaments=fold*fold*fold*2.4;
    float lane=smoothstep(0.42,0.62,fbm(vec3(q*4.0+vec2(warp),1.0)));
    vec3 tone=mix(vec3(0.50,0.22,0.95),vec3(0.08,0.62,0.66),smoothstep(-0.5,0.5,q.y+warp-0.5));
    tone=mix(tone,vec3(0.95,0.30,0.42),smoothstep(0.55,0.9,fold)*0.5);
    vec3 color=tone*sheet*(0.25+filaments)*(1.0-0.75*lane)*0.9;
    float star=length(p-vec2(0.52,0.30));
    color+=vec3(1.9,0.55,0.22)*exp(-star*star/0.0009)+vec3(0.55,0.14,0.06)*exp(-star*9.0);
    return vec4(color,clamp(sheet*(0.35+0.5*lane)+exp(-star*star/0.0007),0.0,0.85));
}

vec4 view(int sector, vec2 local) {
    vec2 p=SKY_MIN+SKY_SIZE*local;
    vec3 d=normalize(vec3(p,-1.0));
    vec4 body;
    if (sector==0) body=kestrel(d);
    else if (sector==4) body=pulsar(p);
    else if (sector==5) body=eclipse(p);
    else if (sector==6) body=iceGiant(d,uSuns[6]);
    else if (sector==7) body=brokenMoon(d,uSuns[7]);
    else if (sector==8) body=veil(p);
    else {
        vec3 dir=sector==1 ? vec3(-0.49,0.16,-1.0) :
                 sector==2 ? vec3(0.54,0.22,-1.0) : vec3(-0.40,-0.17,-1.0);
        float radius=sector==1 ? 2.42 : sector==2 ? 1.85 : 2.64;
        body=world(d,normalize(dir)*10.0,radius,uSuns[sector],sector);
        if (sector==2 || sector==3) {
            vec3 moonDir=sector==2 ? vec3(0.21,0.43,-1) : vec3(-0.07,0.19,-1);
            vec4 moon=world(d,normalize(moonDir)*15.0,sector==2 ? 0.45 : 0.34,uSuns[sector],0);
            body=moon+body*(1.0-moon.a);
            if (sector==2) {
                moon=world(d,normalize(vec3(0.14,0.36,-1))*16.0,0.23,uSuns[2],0);
                body=moon+body*(1.0-moon.a);
            }
        }
    }
    vec2 margin=min(local,1.0-local);
    return body*smoothstep(0.002,0.025,min(margin.x,margin.y));
}

void main() {
    // Ein Viertel je Durchgang: Vier versetzte Durchgänge addieren sich zum geglätteten Bild.
    // Die Abtastungen stehen bewusst nicht im Shader, das vervielfacht dessen Übersetzungszeit.
    vec4 body=view(uCell,vUv+uJitter);
    outColor=0.25*vec4(sqrt(clamp(body.rgb*0.25,0.0,1.0)),body.a);
}
"""

    let private displayFragment =
        """#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uSky;
uniform sampler2D uBodies;
uniform mat4 uView;
uniform vec2 uLens;
uniform bool uHdr;
uniform int uSector;
uniform vec3 uSun;
uniform vec3 uSunColor;
out vec4 outColor;
        """ + Shaders.displayTransform + windowShader + """
// Longitude and colour of the shared galactic band, indexed by sector.
const vec2 YAW[9]=vec2[9](vec2(1,0),vec2(0.362,0.932),vec2(-0.801,0.598),
    vec2(-0.416,-0.909),vec2(0.697,-0.717),vec2(-0.942,-0.335),
    vec2(0.259,-0.966),vec2(-0.500,0.866),vec2(0.940,0.342));
const vec3 TINT[9]=vec3[9](vec3(1),vec3(1.38,0.47,0.29),vec3(0.46,1.27,1.12),
    vec3(0.67,0.85,1.36),vec3(0.94,0.56,1.57),vec3(1.43,0.42,0.49),
    vec3(0.55,0.95,1.30),vec3(1.25,0.80,0.45),vec3(0.80,0.62,1.35));
void main() {
    vec2 xy = (vUv*2.0-1.0)*uLens;
    vec3 ray = normalize(transpose(mat3(uView))*vec3(xy,-1.0));
    vec2 turn=YAW[uSector];
    vec3 galaxy=ray;
    galaxy.xz=mat2(turn.x,turn.y,-turn.y,turn.x)*ray.xz;
    vec2 uv = vec2(atan(galaxy.x,-galaxy.z)/6.28318530718+0.5,asin(clamp(galaxy.y,-1.0,1.0))/3.14159265359+0.5);
    vec3 encoded = texture(uSky,uv).rgb;
    vec3 radiance=4.0*encoded*encoded;
    if (uSector!=0) {
        float luminance=dot(radiance,vec3(0.24,0.62,0.14));
        float nebula=1.0-smoothstep(0.14,0.65,luminance);
        radiance=mix(radiance,mix(radiance,vec3(luminance),0.62)*TINT[uSector],nebula);
    }
    float sun=max(dot(ray,uSun),0.0);
    float sunPeak=max(uSunColor.r, max(uSunColor.g, uSunColor.b));
    vec3 sunTint=mix(vec3(1.0,0.69,0.38), sunPeak > 0.01 ? uSunColor / sunPeak : vec3(1.0), 0.58);
    radiance+=sunTint*(pow(sun,3400.0)*2.1+pow(sun,120.0)*0.15);
    if (ray.z < -0.1) {
        vec2 local=(ray.xy/-ray.z-SKY_MIN)/SKY_SIZE;
        if (all(greaterThanEqual(local,vec2(0))) && all(lessThanEqual(local,vec2(1)))) {
            vec4 body=texture(uBodies,local);
            radiance=radiance*(1.0-body.a)+4.0*body.rgb*body.rgb;
        }
    }
    outColor = vec4(uHdr ? radiance : displayColor(radiance), 1.0);
}
"""

    [<Emit("$0.texImage2D($0.TEXTURE_2D, 0, $0.RGBA, $1, $2, 0, $0.RGBA, $0.UNSIGNED_BYTE, null)")>]
    let private allocate (gl: WebGL2RenderingContext) (width: int) (height: int) : unit = jsNative

    [<Emit("$0.getParameter($0.FRAMEBUFFER_BINDING)")>]
    let private currentFramebuffer (gl: WebGL2RenderingContext) : WebGLFramebuffer = jsNative

    let create (renderer: Renderer) : SkyBackdrop =
        let gl = renderer.Gl
        let vao = gl.createVertexArray ()
        let program = Renderer.linkProgram gl vertex displayFragment
        let framebuffer = gl.createFramebuffer ()
        gl.bindFramebuffer (gl.FRAMEBUFFER, framebuffer)
        gl.disable gl.DEPTH_TEST
        gl.disable gl.CULL_FACE
        gl.disable gl.BLEND
        gl.bindVertexArray vao
        let texture = gl.createTexture ()
        gl.bindTexture (gl.TEXTURE_2D, texture)
        let limit = unbox<int> (gl.getParameter gl.MAX_TEXTURE_SIZE)
        let width = min 3072 limit
        allocate gl width (width / 2)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
        gl.framebufferTexture2D (gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0.0)
        if gl.checkFramebufferStatus gl.FRAMEBUFFER <> gl.FRAMEBUFFER_COMPLETE then
            failwith "Die Himmelstextur konnte nicht erstellt werden."
        let generator = Renderer.linkProgram gl vertex galaxyFragment
        gl.useProgram generator
        gl.viewport (0.0, 0.0, float width, float (width / 2))
        gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)
        gl.deleteProgram generator
        let directions = JS.Constructors.Float32Array.Create (3 * atlasSectors.Length)
        atlasSectors |> Array.iteri (fun i id ->
            Universe.tryFindSector id |> Option.iter (fun sector -> Vec3.writeTo directions (3 * i) sector.Atmosphere.SunDir))
        gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
        gl.deleteFramebuffer framebuffer
        gl.bindVertexArray (unbox null)
        gl.enable gl.DEPTH_TEST
        gl.enable gl.CULL_FACE
        gl.useProgram program
        gl.uniform1i (gl.getUniformLocation (program, "uSky"), 0.0)
        gl.uniform1i (gl.getUniformLocation (program, "uBodies"), 1.0)
        { Program = program
          Texture = texture
          Bodies = unbox null
          BodySuns = directions
          MaxBodyWidth = min 4096 limit
          BodyCell = -1
          BodyWidth = 0
          SectorCells =
              Universe.sectors |> Array.map (fun s ->
                  let template = Universe.skyTemplate s.Id
                  atlasSectors |> Array.findIndex ((=) template))
          Vao = vao
          UView = gl.getUniformLocation (program, "uView")
          UHdr = gl.getUniformLocation (program, "uHdr")
          USector = gl.getUniformLocation (program, "uSector")
          USun = gl.getUniformLocation (program, "uSun")
          USunColor = gl.getUniformLocation (program, "uSunColor")
          ULens = gl.getUniformLocation (program, "uLens") }

    /// Breite der Sektoransicht für eine Zielhöhe in Pixeln: etwa ein Texel je Bildpunkt bei
    /// normalem Sichtfeld, in 512er-Stufen, damit kleine Größenänderungen nichts neu erzeugen.
    let bodyWidth (maxWidth: int) (targetHeight: float) : int =
        let wanted = int (ceil (targetHeight * 2.2 / 512.0)) * 512
        max (min 1024 maxWidth) (min maxWidth wanted)

    let private bodyJitter = [| -0.375; -0.125; 0.125; -0.375; 0.375; 0.125; -0.125; 0.375 |]

    /// Erzeugt die Himmelskörper einer Ansicht neu und stellt Ziel und Viewport wieder her.
    let private bakeBodies (renderer: Renderer) (sky: SkyBackdrop) (cell: int) (width: int) =
        let gl = renderer.Gl
        let height = width * 2 / 3
        let target = currentFramebuffer gl
        if not (isNull (box sky.Bodies)) then gl.deleteTexture sky.Bodies
        let texture = gl.createTexture ()
        gl.bindTexture (gl.TEXTURE_2D, texture)
        allocate gl width height
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
        let framebuffer = gl.createFramebuffer ()
        gl.bindFramebuffer (gl.FRAMEBUFFER, framebuffer)
        gl.framebufferTexture2D (gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0.0)
        gl.disable gl.CULL_FACE
        gl.viewport (0.0, 0.0, float width, float height)
        gl.clearColor (0.0, 0.0, 0.0, 0.0)
        gl.clear gl.COLOR_BUFFER_BIT
        gl.enable gl.BLEND
        gl.blendFunc (gl.ONE, gl.ONE)
        // Der Shader entsteht nur für diese eine Ansicht: Mit allen sechs Welten brauchte seine
        // Übersetzung beim Start über eine Sekunde, obwohl immer nur eine sichtbar ist.
        let generator = Renderer.linkProgram gl vertex (bodyFragment.Replace ("uniform int uCell;", "const int uCell = " + string cell + ";"))
        gl.useProgram generator
        gl.uniform3fv (gl.getUniformLocation (generator, "uSuns[0]"), sky.BodySuns)
        let jitter = gl.getUniformLocation (generator, "uJitter")
        gl.bindVertexArray sky.Vao
        // Vier versetzte Abtastungen je Texel glätten Planetenrand, Ringkanten und Lavaadern.
        for i in 0 .. 3 do
            gl.uniform2f (jitter, bodyJitter.[2 * i] / float width, bodyJitter.[2 * i + 1] / float height)
            gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)
        gl.deleteProgram generator
        gl.disable gl.BLEND
        gl.bindFramebuffer (gl.FRAMEBUFFER, target)
        gl.deleteFramebuffer framebuffer
        gl.viewport (0.0, 0.0, float renderer.Width, float renderer.Height)
        gl.enable gl.CULL_FACE
        renderer.BlendState <- -1
        sky.Bodies <- texture
        sky.BodyCell <- cell
        sky.BodyWidth <- width

    let draw (renderer: Renderer) (sky: SkyBackdrop) (camera: Camera) (sector: int) =
        let gl = renderer.Gl
        gl.disable gl.DEPTH_TEST
        gl.depthMask false
        let cell = if sector >= 0 && sector < sky.SectorCells.Length then sky.SectorCells.[sector] else 0
        // Die dynamische Auflösung ändert nur die Renderskalierung; massgeblich ist die volle Zielhöhe.
        let wanted = bodyWidth sky.MaxBodyWidth (float renderer.Height / max 0.5 renderer.RenderScale)
        if cell <> sky.BodyCell || wanted > sky.BodyWidth then bakeBodies renderer sky cell wanted
        gl.useProgram sky.Program
        gl.uniform1i (sky.UHdr, if renderer.Post.Hdr then 1.0 else 0.0)
        gl.uniform1i (sky.USector, float cell)
        gl.uniform3fv (sky.USun, renderer.Lights.SunDirection)
        gl.uniform3fv (sky.USunColor, renderer.Lights.SunColor)
        gl.uniformMatrix4fv (sky.UView, false, camera.View.Data)
        gl.uniform2f (sky.ULens, 1.0 / float camera.Projection.Data.[0], 1.0 / float camera.Projection.Data.[5])
        gl.activeTexture gl.TEXTURE1
        gl.bindTexture (gl.TEXTURE_2D, sky.Bodies)
        gl.activeTexture gl.TEXTURE0
        gl.bindTexture (gl.TEXTURE_2D, sky.Texture)
        gl.bindVertexArray sky.Vao
        gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)
        gl.enable gl.DEPTH_TEST
        gl.depthMask true
        gl.useProgram renderer.Program
        renderer.DrawCalls <- renderer.DrawCalls + 1
