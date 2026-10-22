# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Підготовлений наскрізний маршрут

```text
submit/click у Client/app.js
  → GET /api/incidents або GET /api/incidents/{id}
  → Presentation/Endpoints/IncidentEndpoints.cs
  → Application/Incidents/IncidentQueries.cs
  → Data/SecureLabDbContext.cs
  → PostgreSQL
  → response DTO у Presentation/Contracts/
  → JSON
  → textContent/createTextNode у Client/app.js
```

## Маршрут severity-summary 

```text
кнопка в Client/index.html
  → handler у Client/app.js
  → GET /api/incidents/severity-summary
  → IncidentEndpoints → IncidentQueries
  → SecureLabDbContext.Incidents / таблиця incidents
  → IncidentSeveritySummaryResponse як JSON
  → textContent у списку
```

## Межі довіри

Доповніть таблицю щонайменше трьома конкретними спостереженнями.

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач контролює введення | Значення `<select>` статусу не є гарантією: клієнт можна обійти, тому остаточна перевірка виконується на сервері |
| Browser client → API | Клієнт і HTTP-запит можна змінити поза UI | Маршрутне обмеження `:guid` для `id`, перевірка `status` у `GetListAsync` (400), 404 для відсутнього ресурсу; будь-який HTTP-клієнт може повторити запит без форми |
| PostgreSQL → API → DOM | У БД може зберігатися раніше введений недовірений текст | DTO та безпечний DOM sink; доповнити: у відповідь потрапляють лише поля DTO (для summary — `severity` і `count`), а в браузері текст виводиться через `textContent`, а не як HTML |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.

## Контракт summary і повернення до seed

Endpoint `GET /api/incidents/severity-summary` повертає групи в порядку критичності: Low → Medium → High → Critical. Застосовано політику «лише наявні групи»: рівень без інцидентів у відповідь не потрапляє, тому на початковому seed повертаються Low, Medium і High по одному інциденту, а Critical відсутній. Щоб повернути навчальний стенд до відомого стану, зупиніть API й виконайте `dotnet run --project src/SecureLab.Api -- --reset-database` (без `--no-build`, якщо код змінювався).
