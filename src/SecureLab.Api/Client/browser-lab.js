const statusBox = document.querySelector("#status");
const titleBox = document.querySelector("#title");
const descriptionBox = document.querySelector("#description");
const commentsBox = document.querySelector("#comments");
const idInput = document.querySelector("#incident-id");
const form = document.querySelector("form"); 

function renderSafeText(target, text) {
    target.textContent = text ?? "";
    return target;
}

function renderComments(container, comments) {
    const fragment = document.createDocumentFragment();
    for (const item of comments ?? []) {
        const paragraph = renderSafeText(document.createElement("p"), item.text);
        fragment.append(paragraph);
    }
    container.replaceChildren(fragment);
}

async function load() {
    const id = idInput.value.trim();
    const response = await fetch(`/api/incidents/${encodeURIComponent(id)}`, {
        credentials: "same-origin",
        headers: { Accept: "application/json" }
    });
    statusBox.textContent = `GET: ${response.status}`;
    if (!response.ok) return;
    const data = await response.json();
    renderSafeText(titleBox, data.title);
    renderSafeText(descriptionBox, data.description);
    renderComments(commentsBox, data.comments);
    if (form) form.elements.description.value = data.description ?? "";
}

document.querySelector("#load")?.addEventListener("click", load);
form?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const body = {
        title: form.elements.title.value,
        description: form.elements.description.value,
        severity: "Low",
        occurredAtUtc: new Date().toISOString()
    };
    const created = await fetch("/api/incidents", {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(body)
    });
    statusBox.textContent = `POST: ${created.status}`;
    if (!created.ok) return;
    const item = await created.json();
    idInput.value = item.id;
    await load();
});