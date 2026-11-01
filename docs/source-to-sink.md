# Карта потоку даних Source-to-Sink (ЛР 5, рівень «Добре»)

## 1. Основний потік: `Incident.Description` (сторінка `/browser-lab.html`)

| Крок | Де виникає або змінюється значення | Дані / доказ | Статус довіри |
|---|---|---|---|
| **Source** | Поле форми `<textarea name="description">` у `src/SecureLab.Api/Client/browser-lab.html` (`form.elements.description.value`) | Канонічний маркер із `resources/lab05-marker.txt`: `<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker', 'executed')">` | Контрольоване користувачем (untrusted) |
| **Request** | `fetch("/api/incidents", { method: "POST" })` у `browser-lab.js` -> контракт `CreateIncidentRequest` | Поле JSON `description` у Request Payload (`POST /api/incidents`, статус `201 Created`) | Контрольоване користувачем (untrusted) |
| **Server transform** | `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs` (`MapLab02Endpoints`) | Перевірка непорожності, `Trim()` та довжини `<= 4000` символів; присвоєння `Description = description` | Не стає trusted після серверної валідації довжини |
| **Storage** | Сутність `Incident.Description` -> таблиця `public.incidents`, колонка `description` у PostgreSQL | Збережений рядок маркера без змін | Збережене значення користувача (persisted untrusted data) |
| **Response** | `GET /api/incidents/{id}` -> `IncidentQueries.GetDetailsAsync` -> `IncidentDetailsResponse.Description` | Поле JSON `description` у Response (`200 OK`, `application/json`); лапки екрановані лише для формату JSON | Недовірене для HTML-інтерпретації браузером |
| **Client transform** | `src/SecureLab.Api/Client/browser-lab.js`, функція `load()` | Читання `const data = await response.json();` та передавання `data.description` | Залежить від обраного DOM API |
| **Sink (до fix)** | `browser-lab.js`: `descriptionBox.innerHTML = data.description ?? ""` | Елемент `<div id="description">`; браузер розбирає рядок HTML-парсером, створює `<img>` і виконує `onerror` | **Небезпечний sink** (HTML context -> Stored / DOM XSS) |
| **Sink (після fix)** | `browser-lab.js`: єдиний helper `renderSafeText(descriptionBox, data.description)` (`target.textContent = text ?? ""`) | Елемент `<div id="description">` містить один текстовий вузол (`childElementCount === 0`), код не виконується | **Безпечний sink** (Text node context) |

---

## 2. Додатковий потік (рівень «Добре»): `IncidentComment.Text` (сторінка `/browser-lab.html`)

| Крок | Де виникає або змінюється значення | Дані / доказ | Статус довіри |
|---|---|---|---|
| **Source** | Навчальний seed у `src/SecureLab.Api/Data/DbSeeder.cs` (`SeedAsync`, константа `Lab05Marker`, коментар `30000000-...0005`; у продукті — ввід автора коментаря) | Однорядковий маркер `<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker', 'executed')">` | Збережене значення користувача / автора коментаря (untrusted) |
| **Request** | `GET /api/incidents/20000000-0000-0000-0000-000000000001` у `browser-lab.js` | Читання інциденту Alice разом із колекцією публічних коментарів | Автентифікований запит читання |
| **Server transform** | `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs` (`GetDetailsAsync`) | Проєкція `comment.Text` у `IncidentCommentResponse(Id, Author, Text, CreatedAtUtc)` | Перевірка авторизації та `IsInternal` не очищує HTML |
| **Storage** | Сутність `IncidentComment.Text` -> таблиця `public.incident_comments`, колонка `text` у PostgreSQL | Збережений рядок `Lab05Marker` для коментаря `30000000-0000-0000-0000-000000000005` | Збережене значення користувача |
| **Response** | `IncidentDetailsResponse.Comments` (`GET /api/incidents/{id}`, статус `200 OK`) | Поле JSON `comments[].text` у тілі відповіді | Недовірене для HTML-інтерпретації |
| **Client transform** | `src/SecureLab.Api/Client/browser-lab.js`, обробка масиву `data.comments` у `load()` | До fix: конкатенація рядків `<p>${item.text}</p>`. Після fix: функція `renderComments` із `DocumentFragment` | До fix — небезпечна конкатенація HTML; після fix — безпечна побудова вузлів |
| **Sink (до fix)** | `browser-lab.js`: `commentsBox.innerHTML = (data.comments ?? []).map(...).join("")` | Елемент `<div id="comments">`; HTML-парсер створює `<img>` всередині `<p>` і виконує `onerror` | **Небезпечний sink** (HTML context -> Stored XSS) |
| **Sink (після fix)** | `browser-lab.js`: `renderComments` -> єдиний helper `renderSafeText(document.createElement("p"), item.text)` + `container.replaceChildren(fragment)` | Елемент `<div id="comments">` містить елементи `<p>` із текстовими вузлами, `<img>` не створюється | **Безпечний sink** (Text node context) |