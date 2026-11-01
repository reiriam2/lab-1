
# Звіт до лабораторної роботи № 5 (DEL-03)


- **Студентка:** Речина Ірина Максимівна, 4 курс, група КБ-43
- **Заявлений рівень:** «Добре»
- **Базовий тег:** `v0.4.0`
- **Робоча гілка:** `lab/5-browser-xss`
- **Vulnerable commit (`VULNERABLE_SHA`):** `ea0b787062739c777e1a1e5863ef7964f21b5975`
- **Fixed commit (`FIXED_SHA`):** `8e1513635836a655df38cf13f90f39d394b68021 `
- **Фінальний тег:** `v0.5.0`

---

## 1. Підсумок виконаної роботи (До -> Першопричина -> Fix -> Retest -> Залишковий ризик)

1. **Карта потоку та інвентаризація (`docs/source-to-sink.md`):** Простежено повний шлях даних для основного поля `Incident.Description` (від форми створення інциденту до таблиці `incidents` і клієнтського рендерингу) та додаткового поля `IncidentComment.Text` (від `DbSeeder.cs` і таблиці `incident_comments` до блоку коментарів на `/browser-lab.html`).
2. **Стан «До» (`VULNERABLE_SHA`):** У наданому `browser-lab.js` значення `data.description` та `data.comments[].text` записувалися у властивість `innerHTML`. Відтворено нешкідливий локальний маркер (`resources/lab05-marker.txt`), який створив елемент `<img>` і виконав `onerror`-обробник, встановивши `data-lab05-marker="executed"` як у сеансі автора `alice`, так і у сеансі аналітика `morgan` (`Analyst`).
3. **Виправлення (`FIXED_SHA`):** У `src/SecureLab.Api/Client/browser-lab.js` обидва небезпечні `innerHTML` замінено на єдиний контекстно-безпечний helper `renderSafeText(target, text)` (який використовує `textContent`) та функцію `renderComments` із `document.createElement("p")`, `DocumentFragment` і `replaceChildren()`. Збережені дані в БД та JSON-відповідях API не модифікувалися.
4. **Retest та позитивна регресія (`REG-XSS-01`):** Після виправлення канонічний маркер у `Description` та `IncidentComment.Text` відображається на екрані як звичайний текст без створення `<img>`, а атрибут `data-lab05-marker` залишається `null`. Перевірено коректне відображення складного тексту з лапками, апострофом, `<`, `>`, `<b>` та Unicode (`Київ Ω`).
5. **CORS, Preflight та Credentials (`REG-CORS-01`):** Піднято навчальні probes на `http://localhost:4173` і `http://localhost:4174`. У `Program.cs` для середовища `Development` налаштовано точну політику `Lab05BrowserProbe` (`WithOrigins("http://localhost:4173")`, `WithMethods("GET")`, `WithHeaders("X-Lab-Cors-Probe")`, `AllowCredentials()`). Підтверджено роботу простих запитів, preflight `OPTIONS` і читання захищеного `/api/incidents` з кукі для `4173`, блокування читання у браузері для `4174` та незалежність прямого клієнта `curl.exe` від правил браузерного SOP/CORS.
6. **CSP та Security Headers:** До спільної middleware у `Program.cs` додано заголовок `Content-Security-Policy: default-src 'self'; frame-ancestors 'none'` в режимі enforcement (із задокументованим винятком для `/scalar` у Development) та перевірено наявні `X-Content-Type-Options: nosniff` і `Referrer-Policy: no-referrer`.

---

## 2. Письмовий Review: Розрізнення XSS, CORS, CSRF та Authorization (Етап 3, крок 8)

- **XSS (Cross-Site Scripting):** Порушення межі між даними і кодом у браузері, коли недовірений рядок (з форми, URL або БД) вставляється у небезпечний sink (`innerHTML`) і виконується як JavaScript у контексті origin вашого сайту (`http://localhost:5080`). Коли скрипт уже виконується через XSS на `localhost:5080`, для нього всі запити до API є same-origin, тому ні CORS, ні CSRF-токени не захистять активний сеанс від дій цього скрипту. Основний захист — контекстно-безпечний рендеринг (`textContent`, `createElement`) та CSP як додатковий рубіж (defense in depth).
- **CORS (Cross-Origin Resource Sharing):** Механізм браузера та HTTP-заголовків (`Access-Control-Allow-*`), який лише дозволяє або забороняє JavaScript-коду з іншого origin (наприклад, `http://localhost:4173`) **прочитати відповідь** від API (`http://localhost:5080`). Як показав наш дослід із портом `4174` та `curl.exe`, CORS **не зупиняє** доставку та виконання простих запитів на сервері (запит із кукі все одно доходить до API і виконується, браузер лише ховає результат від JS).
- **CSRF (Cross-Site Request Forgery):** Атака, за якої стороння сторінка змушує браузер користувача надіслати змінювальний запит (`POST`/`PUT`/`DELETE`) до API, до якого браузер автоматично прикріплює сеансову cookie. Оскільки при атаці CSRF зловмиснику достатньо самого факту зміни стану на сервері і йому не потрібно читати тіло відповіді через JavaScript, заборона читання відповіді через CORS не захищає від простих CSRF-запитів; для цього потрібні окремі antiforgery-механізми та політика `SameSite` (предмет ЛР 6).
- **Authorization (Серверна авторизація):** Перевірка на стороні сервера (`IncidentAccess` у ЛР 4), чи має поточний автентифікований `HttpContext.User` право виконати дію над конкретним ресурсом. Вона діє для всіх клієнтів (і браузера, і `curl`), не замінюється CORS і не перевіряє безпеку побудови DOM у браузері.

---

## 3. Відповіді на контрольні питання

1. **Чому value з БД може залишатися недовіреним?** Тому що база даних лише зберігає байти тексту, які раніше ввів користувач; сам факт запису в PostgreSQL або проходження перевірки довжини не перетворює користувацький текст на безпечну HTML-розмітку.
2. **Що таке source, transformation і sink у вашому flow?** Source — поле форми `<textarea name="description">` (та seed коментаря); transformation — передавання через JSON DTO, збереження в БД та читання `response.json()`; sink — операція вставки в DOM (`innerHTML` до виправлення, `textContent` у `renderSafeText` після виправлення).
3. **Чим stored XSS відрізняється від reflected та DOM-based XSS?** При Stored XSS шкідливий рядок зберігається на сервері (у БД) і спрацьовує пізніше під час перегляду іншими користувачами; при Reflected XSS — миттєво повертається у відповіді на поточний запит; при DOM-based XSS — клієнтський JavaScript сам бере дані з джерела і передає в небезпечний DOM sink (наш потік поєднує Stored походження з БД та DOM-based виконання через клієнтський `innerHTML`).
4. **Чому блокування `<script>` не усуває XSS?** Бо HTML-парсер за `innerHTML` взагалі не виконує теги `<script>`, зате виконує JavaScript у десятках атрибутів подій інших тегів (`<img onerror="...">`, `<svg onload="...">` тощо).
5. **Коли обирають `textContent`, а коли потрібен sanitizer?** `textContent` обирають завжди, коли поле за бізнес-логікою є звичайним текстом; sanitizer (наприклад, DOMPurify з білим списком тегів) потрібен лише тоді, коли продукт явно вимагає підтримувати форматований користувацький HTML.
6. **Чому sanitizer не пишуть regular expression?** Бо регулярні вирази не здатні відтворити складний автомат станів браузерного HTML-парсера, вкладені теги, кодування сутностей та автовиправлення розмітки браузером.
7. **З яких трьох компонентів складається origin?** Схема (`protocol` / `scheme`) + хост (`host` / `domain`) + порт (`port`).
8. **Що browser надсилає в CORS preflight і хто його ініціює?** Ініціює автоматично сам браузер перед «непростим» запитом; це запит `OPTIONS` із заголовками `Origin`, `Access-Control-Request-Method` та (за наявності нестандартних заголовків) `Access-Control-Request-Headers`.
9. **Хто оголошує CORS-політику і хто її застосовує?** Оголошує сервер API у заголовках відповіді `Access-Control-Allow-*`, а примусово застосовує (дозволяє чи блокує доступ JS до відповіді) виключно браузер.
10. **Чому `curl` може прочитати API response, коли browser script отримує CORS error?** Бо `curl` є прямим HTTP-клієнтом і не має браузерного рушія JavaScript та політики Same-Origin Policy (SOP).
11. **Чому CORS не є authorization і не замінює CSRF-захист?** CORS перевіряє лише origin веб-сторінки для читання відповіді, але не перевіряє права користувача на ресурс і не зупиняє відправку простих змінювальних запитів із cookie.
12. **Чому wildcard origin `*` не поєднують із credentials?** Дозвіл `*` разом із `Allow-Credentials: true` дозволив би будь-якому сайту в інтернеті читати приватні відповіді API від імені залогіненого користувача; тому стандарт CORS і ASP.NET Core забороняють таку комбінацію.
13. **Яку межу має CSP і чому вона не замінює safe DOM rendering?** CSP (`default-src 'self'`) блокує виконання inline-обробника `onerror`, але не заважає `innerHTML` зламати верстку й вставити довільні HTML-елементи у DOM (ін'єкція розмітки лишається).
14. **Який risk зменшують `nosniff`, `Referrer-Policy` і `frame-ancestors`?** `nosniff` запобігає вгадуванню MIME-типу браузером; `Referrer-Policy: no-referrer` запобігає витоку URL поточної сторінки в заголовку `Referer`; `frame-ancestors 'none'` забороняє вбудовування сторінки в `<iframe>`, захищаючи від Clickjacking.