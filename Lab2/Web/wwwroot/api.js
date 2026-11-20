function token() {
  const value = sessionStorage.getItem("token");
  if (!value) return "";
  try {
    const payload = JSON.parse(atob(value.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    if (payload.exp > Date.now() / 1000) return value;
  } catch { /* malformed token */ }
  sessionStorage.removeItem("token");
  return "";
}

addEventListener("pageshow", () => {
  const signedIn = Boolean(token());
  for (const el of document.querySelectorAll("[data-member], [data-guest]"))
    el.hidden = signedIn !== el.hasAttribute("data-member");
});

async function downloadFile(id, name) {
  const response = await api("/api/download/" + id, { headers: authHeaders() });
  if (!response.ok) return;
  const attachment = response.headers.get("Content-Disposition") ?? "";
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(attachment)?.[1];
  if (encoded) { try { name = decodeURIComponent(encoded); } catch { /* use listed name */ } }
  const href = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = href;
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(href), 0);
}

function authHeaders(extra) {
  const headers = Object.assign({}, extra);
  const value = token();
  if (value) headers.Authorization = "Bearer " + value;
  return headers;
}

async function api(path, options) {
  const response = await fetch(path, options);
  const message = document.querySelector(".status");
  if (message) message.textContent = response.ok ? "Готово" : "Ошибка " + response.status;
  return response;
}

function readFields(form) {
  const data = {};
  for (const field of form.querySelectorAll("input")) {
    const value = field.value.trim();
    if (field.required && !value) throw new Error("Заполните " + field.name);
    if (field.maxLength > 0 && value.length > field.maxLength) throw new Error("Слишком длинное поле");
    data[field.name] = value;
  }
  return data;
}
