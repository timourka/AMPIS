function cell(text) {
  const item = document.createElement("td");
  item.textContent = text == null ? "" : String(text);
  return item;
}

function button(label, run) {
  const el = document.createElement("button");
  el.type = "button";
  el.textContent = label;
  el.addEventListener("click", async () => {
    el.disabled = true;
    try { await run(); }
    catch { document.querySelector(".status").textContent = "Не удалось выполнить действие"; }
    finally { el.disabled = false; }
  });
  return el;
}

const form = document.querySelector("form");
const pagination = document.querySelector(".pagination");
for (const [key, value] of new URLSearchParams(location.search)) {
  const field = form.elements.namedItem(key);
  if (field) field.value = value;
}

function renderPages(params, total) {
  pagination.replaceChildren();
  const current = Number(params.get("page"));
  const last = Math.ceil(total / Number(params.get("size")));
  if (last <= 1) return;

  const pages = [1];
  const firstVisible = last <= 7 ? 2 : Math.max(2, current - 2);
  const lastVisible = last <= 7 ? last - 1 : Math.min(last - 1, current + 2);
  for (let page = firstVisible; page <= lastVisible; page++) pages.push(page);
  pages.push(last);
  for (const [index, page] of pages.entries()) {
    if (index > 0 && page - pages[index - 1] > 1) {
      const gap = document.createElement("span");
      gap.textContent = "…";
      pagination.append(gap);
    }
    const link = document.createElement(page === current ? "span" : "a");
    link.textContent = String(page);
    if (page === current) link.setAttribute("aria-current", "page");
    else {
      const target = new URLSearchParams(params);
      target.set("page", String(page));
      link.href = "?" + target;
    }
    pagination.append(link);
  }
}

async function load(params) {
  const q = document.querySelector("[name=q]");
  const sort = document.querySelector("[name=sort]");
  const dir = document.querySelector("[name=dir]");
  const size = form.elements.namedItem("size");
  params.set("q", q.value.trim().slice(0, 100));
  params.set("sort", ["name", "owner", "downloads"].includes(sort.value) ? sort.value : "name");
  params.set("dir", dir.value === "desc" ? "desc" : "asc");
  const page = Number(params.get("page"));
  params.set("page", String(Number.isInteger(page) && page > 0 ? page : 1));
  size.value = ["2", "4", "8"].includes(size.value) ? size.value : "2";
  params.set("size", size.value);
  const response = await api("/api/files?" + params.toString(), { headers: authHeaders() });
  if (!response.ok) return;
  history.replaceState(null, "", "?" + params);
  const body = await response.json();
  const rows = document.querySelector("tbody");
  rows.replaceChildren();
  for (const item of body.items || []) {
    const row = document.createElement("tr");
    const actions = cell("");
    actions.className = "actions";
    actions.append(button("Скачать", () => downloadFile(item.id, item.name)));
    if (item.isOwner) {
      actions.append(button("Удалить", async () => {
        if (!confirm(`Удалить «${item.name}»?`)) return;
        const result = await api("/api/files/" + item.id, { method: "DELETE", headers: authHeaders() });
        if (result.ok) {
          row.remove();
          const total = document.querySelector(".total");
          total.textContent = String(Math.max(0, Number(total.textContent) - 1));
          renderPages(params, Number(total.textContent));
        }
      }), button("Статистика", () => { location.href = "/stats?id=" + encodeURIComponent(item.id); }));
    }
    row.append(cell(item.name), cell(item.owner), cell(item.downloads), actions);
    rows.append(row);
  }
  document.querySelector(".total").textContent = String(body.total ?? 0);
  renderPages(params, body.total ?? 0);
}

function applyFilters() {
  load(new URLSearchParams());
}

form.addEventListener("submit", event => {
  event.preventDefault();
  applyFilters();
});
form.elements.namedItem("size").addEventListener("change", applyFilters);
addEventListener("pageshow", () => load(new URLSearchParams(location.search)));
