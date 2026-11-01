# Доказовий комплект безпеки браузера (DEL-01 — ЛР 5)

## 1. Стенд і Git-стан
- **Студентка:** Речина Ірина Максимівна (КБ-43)
- **Заявлений рівень:** «Добре»
- **Базовий тег входу:** `v0.4.0`
- **Робоча гілка:** `lab/5-browser-xss`
- **Scaffold release:** `lab-05-start-v1` (`.scaffolds/lab-05.json`)
- **Vulnerable commit (`VULNERABLE_SHA`):** `ea0b787062739c777e1a1e5863ef7964f21b5975`
- **Fixed commit (`FIXED_SHA`):** `8e1513635836a655df38cf13f90f39d394b68021 `
- **Підсумковий тег:** `v0.5.0`
- **Топологія локального стенда:**
  - Основний API та same-origin клієнт: `http://localhost:5080`
  - Дозволений навчальний CORS probe (Development only): `http://localhost:4173`
  - Недозволений контрольний CORS probe: `http://localhost:4174`

---

## 2. Інвентаризація DOM Sinks та гіпотеза (Етап 1)

| Місце у клієнті | Дані, що потрапляють у sink | Категорія даних | Контекст | Рішення |
|---|---|---|---|---|
| `Client/app.js`: `createTextElement`, `summaryList`, `incidentList` | `incident.title`, `incident.description`, `comment.text`, `entry.severity` | збережене значення користувача / server-generated | Text node (`textContent`) | Вже безпечно (використовується `createElement` + `textContent`) |
| `Client/browser-lab.js`: `statusBox.textContent`, `titleBox.textContent` | `response.status`, `data.title` | server-generated / збережене значення користувача | Text node (`textContent`) | Вже безпечно (`textContent`) |
| `Client/browser-lab.js`: `descriptionBox.innerHTML` (до fix) | `data.description` (`Incident.Description`) | збережене значення користувача | HTML parser (`innerHTML`) | **Небезпечно** -> замінити на `renderSafeText` (`textContent`) |
| `Client/browser-lab.js`: `commentsBox.innerHTML` (до fix) | `data.comments[].text` (`IncidentComment.Text`) | збережене значення користувача | HTML parser (`innerHTML`) | **Небезпечно** -> замінити на `renderComments` + `renderSafeText` + `replaceChildren` |

**Структурована гіпотеза:**
> Значення з поля форми `form.elements.description` (або seed-коментаря `IncidentComment.Text`) зберігається в таблицях `incidents.description` та `incident_comments.text` у PostgreSQL, повертається через поля `description` та `comments[].text` JSON-відповіді `GET /api/incidents/{id}` і передається у `descriptionBox.innerHTML` та `commentsBox.innerHTML` у `browser-lab.js` як HTML context; тому за умови наявності HTML-розмітки з обробником подій (зокрема `<img src=x onerror="...">`) браузер інтерпретує дані не як звичайний текст, а як DOM-елемент і виконує JavaScript у сеансі поточного глядача.

---

## 3. Security Finding та докази XSS До / Після (Етап 2)

| Частина | Опис |
|---|---|
| **Актив** | Активний браузерний сеанс користувача (зокрема привілейованого аналітика `morgan` із роллю `Analyst`), DOM-дерево сторінки та дані інцидентів, доступні від імені поточного користувача. |
| **Передумови** | Локальний обліковий запис автора (`alice`, роль `Reporter`), доступ до створення інциденту `POST /api/incidents` (та seed-коментаря в `DbSeeder.cs`). |
| **Першопричина** | Передавання недовірених збережених значень `data.description` та `data.comments[].text` у небезпечний HTML sink `innerHTML` у файлі `src/SecureLab.Api/Client/browser-lab.js` замість текстових вузлів DOM. |
| **Вплив на стенді (PoC на `VULNERABLE_SHA`)** | Канонічний нешкідливий маркер із `resources/lab05-marker.txt` створив елемент `<img src="x" onerror="...">` у `#description` та `#comments` і встановив атрибут `data-lab05-marker="executed"` на `#lab05-marker-target` як у сеансі автора `alice`, так і в сеансі аналітика `morgan` (`'executed'`). Поєднання Stored XSS (збереження в БД) та DOM-based sink (`innerHTML` у клієнтському JS). |
| **Виправлення** | У `src/SecureLab.Api/Client/browser-lab.js` реалізовано єдиний контекстно-безпечний helper `renderSafeText(target, text)` на основі `textContent` та функцію `renderComments(container, comments)` на основі `document.createElement("p")`, `DocumentFragment` і `container.replaceChildren(fragment)`. Усі входження `innerHTML` повністю усунено. |
| **Retest та позитивна регресія** | На `FIXED_SHA` той самий JSON-відгук із тим самим збереженим маркером відображається на екрані символ у символ як текст; у `#description` та `#comments` відсутні елементи `<img>` (`childElementCount === 0` для опису), а перевірка `#lab05-marker-target` повертає `null`. Складний текст (`O'Brien`, `"терміново"`, `5 < 7`, `9 > 3`, `Київ Ω`, `<b>жирний</b>`) відображається без спотворень. |
| **Залишковий ризик** | Безпечний рендеринг у `#description` та `#comments` захищає лише текстовий контекст цих елементів; якби у майбутньому додалися динамічні посилання `href`/`src`, для них `textContent` не підходить і знадобилася б перевірка протоколу URL (`http:`/`https:`). Крім того, у Development маршрут `/scalar` використовує сторонній UI-пакет. |

---

## 4. Докази Origin, CORS, Preflight та Credentials (Етап 3)

**Команди запуску навчальних probes з кореня проєкту:**
```powershell
py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe
py -3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe