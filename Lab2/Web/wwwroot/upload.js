const limit = 1048576;
const drop = document.querySelector(".drop");
const picker = document.querySelector("input[type=file]");
const chosen = document.querySelector(".chosen");
let selected = null;

function take(file) {
  selected = file && file.size <= limit ? file : null;
  if (!file) return;
  if (file.size > limit) {
    chosen.textContent = "Файл больше 1 МиБ";
    picker.value = "";
    return;
  }
  chosen.textContent = file.name;
}

drop.addEventListener("dragover", event => { event.preventDefault(); });
drop.addEventListener("drop", event => {
  event.preventDefault();
  take(event.dataTransfer.files[0]);
});
drop.addEventListener("keydown", event => {
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    picker.click();
  }
});
picker.addEventListener("change", () => take(picker.files[0]));

document.querySelector("form").addEventListener("submit", async event => {
  event.preventDefault();
  const file = selected;
  if (!file || file.size > limit) {
    chosen.textContent = "Выберите файл не больше 1 МиБ";
    return;
  }
  const body = new FormData();
  body.append("file", file);
  const response = await api("/api/upload", { method: "POST", headers: authHeaders(), body });
  if (response.ok) {
    const data = await response.json();
    document.querySelector(".id").textContent = data.id;
  }
});
