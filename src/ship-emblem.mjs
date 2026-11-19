const storageKey = "space2601ae.ship-emblem";
const size = 256;
const fileLimit = 5 * 1024 * 1024;
const dataPrefix = "data:image/png;base64,";

async function imageFormat(file) {
  const bytes = new Uint8Array(await file.slice(0, 16).arrayBuffer());
  const matches = (offset, signature) => signature.every((byte, index) => bytes[offset + index] === byte);
  if (matches(0, [137, 80, 78, 71, 13, 10, 26, 10])) return "image/png";
  if (matches(0, [255, 216, 255])) return "image/jpeg";
  if (matches(0, [82, 73, 70, 70]) && matches(8, [87, 69, 66, 80])) return "image/webp";
  return "";
}

export function mountShipEmblem(updateTexture) {
  const template = document.getElementById("ship-emblem-controls");
  const panels = Array.from(document.querySelectorAll("[data-ship-emblem-settings]"), (panel) => {
    panel.append(template.content.cloneNode(true));
    return {
      input: panel.querySelector("input[type=file]"),
      preview: panel.querySelector("img"),
      upload: panel.querySelector("[data-emblem-upload]"),
      remove: panel.querySelector("[data-emblem-remove]"),
      status: panel.querySelector("[role=status]"),
    };
  });
  let latestUpload = 0;
  let source = "";
  try {
    const stored = localStorage.getItem(storageKey);
    if (stored?.startsWith(dataPrefix) && stored.length <= 400_000) source = stored;
  } catch {}

  function report(message) {
    for (const { status } of panels) status.textContent = message;
  }

  function show(url, image) {
    source = url;
    updateTexture(image);
    for (const { preview, upload, remove } of panels) {
      if (url) preview.src = url;
      else preview.removeAttribute("src");
      preview.hidden = !url;
      if (!url && document.activeElement === remove) upload.focus();
      remove.hidden = !url;
    }
  }

  async function upload(file) {
    const version = ++latestUpload;
    if (file.size > fileLimit) {
      report("Wähle ein Bild bis 5 MB.");
      return;
    }
    report("Bild wird geladen …");
    try {
      const format = await imageFormat(file);
      if (version !== latestUpload) return;
      if (!format) {
        report("Wähle ein PNG-, JPEG- oder WebP-Bild.");
        return;
      }
      const bitmap = await createImageBitmap(file.slice(0, file.size, format));
      const canvas = document.createElement("canvas");
      canvas.width = canvas.height = size;
      try {
        if (version !== latestUpload) return;
        if (Math.max(bitmap.width, bitmap.height) > 4096) {
          report("Das Bild darf pro Seite höchstens 4096 Pixel haben.");
          return;
        }
        const scale = size / Math.max(bitmap.width, bitmap.height);
        const width = bitmap.width * scale;
        const height = bitmap.height * scale;
        canvas.getContext("2d").drawImage(bitmap, (size - width) / 2, (size - height) / 2, width, height);
      } finally {
        bitmap.close();
      }
      const url = canvas.toDataURL("image/png");
      show(url, canvas);
      try {
        localStorage.setItem(storageKey, url);
        report("Emblem gespeichert.");
      } catch {
        report("Emblem aktiv. Der Browser konnte es nur für diese Sitzung übernehmen.");
      }
    } catch {
      if (version === latestUpload) report("Das Bild konnte nicht gelesen werden.");
    }
  }

  for (const { input, upload: button, remove } of panels) {
    button.addEventListener("click", () => input.click());
    input.addEventListener("change", () => {
      const file = input.files[0];
      input.value = "";
      if (file) void upload(file);
    });
    remove.addEventListener("click", () => {
      ++latestUpload;
      show("", null);
      try {
        localStorage.removeItem(storageKey);
        report("Emblem entfernt.");
      } catch {
        report("Emblem für diese Sitzung entfernt.");
      }
    });
  }

  if (source) {
    const url = source;
    const image = new Image();
    image.src = url;
    image.decode().then(() => {
      if (source === url && image.naturalWidth === size && image.naturalHeight === size) show(url, image);
    }).catch(() => {});
  }
}
