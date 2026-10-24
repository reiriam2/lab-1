const stateLabel = document.querySelector("#state");
const result = document.querySelector("#result");
const profile = document.querySelector("#profile");
function apiFetch(path, options = {}) {
    const url = new URL(path, location.origin);
    if (url.origin !== location.origin) {
        throw new Error("Потрібна адреса цього застосунку.");
    }
    return fetch(url, { ...options, credentials: "same-origin" });
}

function jsonRequest(method, payload) {
    return {
        method,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    };
}

function setResult(text) {
    result.textContent = text;
}

function showState(state) {
    const labels = { anonymous: "не ввійшли", "signed-in": "сеанс активний", unknown: "невідомо" };
    stateLabel.textContent = labels[state] ?? "невідомо";
}

function clearProfile() {
    profile.replaceChildren();
}

function renderProfile(data) {
    clearProfile();
    const rows = [["Id", data.userId], ["Ім'я", data.userName], ["Роль", data.role]];
    for (const [label, value] of rows) {
        const term = document.createElement("dt");
        term.textContent = label;
        const description = document.createElement("dd");
        description.textContent = String(value);
        profile.append(term, description);
    }
}

async function problemText(response) {
    try {
        const problem = await response.json();
        if (problem.errors) return Object.values(problem.errors).flat().join(" ");
        return problem.detail ?? problem.title ?? `Статус ${response.status}`;
    } catch {
        return `Статус ${response.status}`;
    }
}

async function refreshState() {
    try {
        const response = await apiFetch("/api/auth/session-check");
        if (response.status === 204) {
            showState("signed-in");
        } else if (response.status === 401) {
            showState("anonymous");
            clearProfile();
        } else {
            showState("unknown");
        }
    } catch {
        showState("unknown");
    }
}

document.querySelector("#register").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const payload = Object.fromEntries(new FormData(form));
    form.elements.password.value = "";
    try {
        const response = await apiFetch("/api/auth/register", jsonRequest("POST", payload));
        if (response.status === 201) {
            form.reset();
            setResult("Реєстрація успішна (201). Тепер увійдіть.");
        } else {
            setResult(`Реєстрація: ${response.status}. ${await problemText(response)}`);
        }
    } catch {
        setResult("Помилка мережі під час реєстрації.");
    } finally {
        delete payload.password;
    }
});

document.querySelector("#login").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const payload = Object.fromEntries(new FormData(form));
    form.elements.password.value = "";
    try {
        const response = await apiFetch("/api/auth/login", jsonRequest("POST", payload));
        setResult(`Login: ${response.status}`);
    } catch {
        setResult("Помилка мережі під час входу.");
    } finally {
        delete payload.password;
        await refreshState();
    }
});

async function loadProfile() {
    try {
        const response = await apiFetch("/api/me");
        if (response.status === 401) {
            clearProfile();
            showState("anonymous");
            setResult("Потрібен вхід (401).");
            return;
        }
        const type = response.headers.get("Content-Type") ?? "";
        if (!response.ok || !type.includes("application/json")) {
            clearProfile();
            setResult(`Помилка профілю: ${response.status}`);
            return;
        }
        renderProfile(await response.json());
        showState("signed-in");
        setResult("Профіль отримано.");
    } catch {
        clearProfile();
        setResult("Помилка мережі.");
    }
}
document.querySelector("#me").addEventListener("click", loadProfile);

document.querySelector("#create").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const local = data.get("occurredAtUtc");
    // У тілі немає власника, ролі, статусу та id.
    const payload = {
        title: data.get("title"),
        description: data.get("description"),
        severity: data.get("severity"),
        occurredAtUtc: local ? new Date(local).toISOString() : null
    };
    try {
        const response = await apiFetch("/api/incidents", jsonRequest("POST", payload));
        if (response.status === 201) {
            const created = await response.json();
            form.reset();
            setResult(`Створено (201): ${created.title}`);
        } else if (response.status === 401) {
            showState("anonymous");
            clearProfile();
            setResult("Потрібен вхід (401).");
        } else {
            setResult(`Створення: ${response.status}. ${await problemText(response)}`);
        }
    } catch {
        setResult("Помилка мережі під час створення.");
    }
});

document.querySelector("#logout").addEventListener("click", async () => {
    try {
        const response = await apiFetch("/api/auth/logout", { method: "POST" });
        setResult(`Logout: ${response.status}`);
    } catch {
        setResult("Помилка мережі під час виходу.");
    } finally {
        clearProfile();
        document.querySelector("#create").reset();
        await refreshState();
    }
});

refreshState();