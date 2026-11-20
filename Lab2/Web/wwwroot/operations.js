const path = location.pathname;
const downloading = path === "/download";
const rows = document.querySelector("tbody");
const status = document.querySelector(".status");

function action(label, run) {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = label;
  button.addEventListener("click", async () => {
    button.disabled = true;
    try { await run(); }
    catch { status.textContent = "Не удалось выполнить действие"; }
    finally { button.disabled = false; }
  });
  return button;
}

async function loadFiles() {
  rows.replaceChildren();
  const response = await api(downloading ? "/api/downloads" : "/api/my-files", { headers: authHeaders() });
  if (response.status === 401) status.textContent = "Войдите, чтобы увидеть свои файлы";
  if (response.status === 503) status.textContent = downloading ? "Сервис скачивания недоступен" : "Сервис удаления недоступен";
  if (!response.ok) return;

  const files = await response.json();
  status.textContent = files.length ? "" : "Нет файлов";
  for (const file of files) {
    const row = rows.insertRow();
    row.insertCell().textContent = file.name;
    const actions = row.insertCell();
    actions.className = "actions";
    if (downloading) {
      actions.append(action("Скачать", () => downloadFile(file.id, file.name)));
      continue;
    }
    actions.append(action("Удалить", async () => {
      if (!confirm(`Удалить «${file.name}»?`)) return;
      const result = await api("/api/files/" + file.id, { method: "DELETE", headers: authHeaders() });
      if (result.ok) {
        row.remove();
        if (!rows.rows.length) status.textContent = "Нет файлов";
      }
    }));
    if (path === "/my-files") {
      const link = document.createElement("a");
      link.href = "/stats?id=" + encodeURIComponent(file.id);
      link.textContent = "Статистика";
      actions.append(link);
    }
  }
}

addEventListener("pageshow", () => loadFiles().catch(() => {
  status.textContent = "Не удалось загрузить список файлов";
}));
