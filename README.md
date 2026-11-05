# Task Management API

An ASP.NET Core .NET 10 API for registering users, logging in with JWTs, and managing personal tasks. The API uses Dapper and Npgsql to query PostgreSQL. Docker Compose runs the API and database as separate services.

## Start existing containers

Open Docker Desktop, then run:

```sh
docker start taskmanagement-postgres-1 taskmanagement-api-1
curl -i http://127.0.0.1:5080/health/database
```

Expect `200 OK` and `{"status":"ok"}`. If PostgreSQL is still starting, wait a few seconds and retry. The container names above are for the default Compose project in the `Task Management` folder. Use `docker ps -a` to find the names for a differently named checkout.

## First-time setup

You need Docker with Compose and OpenSSL. Run these commands from the project directory with Docker running. This setup uses host ports `5080` and `5433`.

1. Generate local configuration for a **new PostgreSQL volume**. Keep these values private and retain the password and signing key in a secure local place if you plan to recreate the containers later.

   ```sh
   export POSTGRES_PASSWORD="$(openssl rand -hex 24)"
   export TASK_DB_CONNECTION_STRING="Host=postgres;Port=5432;Database=task_management;Username=task_app;Password=${POSTGRES_PASSWORD}"
   export TASK_JWT_SIGNING_KEY="$(openssl rand -base64 32)"
   ```

   For an existing volume, use its original PostgreSQL password. PostgreSQL sets that password when it initializes the volume. Keep using the same signing key if existing tokens should remain valid until they expire.

2. Validate the configuration and start both services.

   ```sh
   docker compose config --quiet
   docker compose up -d --build
   docker compose ps
   ```

3. Wait for PostgreSQL to accept connections. If it reports that it is still starting, retry the command.

   ```sh
   docker compose exec postgres pg_isready -h 127.0.0.1 -p 5432 -U task_app -d task_management
   ```

4. Apply the schema scripts in numeric order once per new database, then check connectivity.

   ```sh
   docker compose exec -T postgres psql -U task_app -d task_management \
     -v ON_ERROR_STOP=1 --single-transaction --file=- < db/001_initial_schema.sql

   docker compose exec -T postgres psql -U task_app -d task_management \
     -v ON_ERROR_STOP=1 --single-transaction --file=- < db/002_add_user_name.sql

   docker compose exec -T postgres psql -U task_app -d task_management \
     -v ON_ERROR_STOP=1 --single-transaction --file=- < db/003_add_task_due_date_and_priority.sql

   docker compose exec -T postgres psql -U task_app -d task_management \
     -v ON_ERROR_STOP=1 --single-transaction --file=- < db/004_add_task_list_index.sql

   curl -i http://127.0.0.1:5080/health/database
   ```

The API listens on port `8080` in its container and is available at `http://127.0.0.1:5080` on your machine. It reaches PostgreSQL at `postgres:5432` on the Compose network. PostgreSQL stores its data in the `postgres_data` volume.

## Run the frontend

Use Node.js 22.19 or a newer version supported by Vite. Start the existing Docker containers first, then run these commands from the repository root.

```sh
cd frontend
npm ci
npm run dev
```

Open the local URL printed by Vite, usually `http://localhost:5173/`. Keep that terminal running during development. Vite forwards `/api` requests to the Dockerized API at `http://127.0.0.1:5080`.

Choose **Create account** to register, then sign in. New accounts see their saved name in the greeting. Accounts created before names were added see **My tasks**. Tokens stay in React memory, so refresh and sign-out return to the login screen. A protected request receiving `401` also clears the session and shows an expiry message.

From `frontend/`, run `npm run build` to type-check and build the frontend, and `npm run lint` to check the code.

## Try the API in Postman

1. In Postman, choose **Import** and select [TaskManagement.postman_collection.json](postman/TaskManagement.postman_collection.json).
2. In the collection’s **Variables** tab, leave `baseUrl` as `http://127.0.0.1:5080` for the default Docker setup. Set `emailA` and `emailB` to two unused test email addresses and `passwordA` and `passwordB` to local test passwords of 12–128 characters. Keep these values local.
3. Run the collection in order. The login requests fill `tokenA` and `tokenB`, and the create request fills `taskId`. The run checks task CRUD, cross-account access, unauthenticated requests, and request validation.

The collection deletes the task it creates. It leaves the registered test accounts in the database, so use two new email addresses for each full run. To use existing accounts, skip the two registration requests.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/tasks` | List your tasks |
| POST | `/api/tasks` | Create a task from `title` and an optional `description` |
| GET | `/api/tasks/{id}` | Fetch one of your tasks |
| PUT | `/api/tasks/{id}` | Replace `title`, `description`, and `isCompleted` |
| DELETE | `/api/tasks/{id}` | Delete one of your tasks and return `204 No Content` |

## Stop or rebuild

Stop the existing containers with `docker stop taskmanagement-api-1 taskmanagement-postgres-1`. To pick up C# changes, export the same Compose configuration in your current terminal and run `docker compose up -d --build`. Apply the schema only when setting up a new database.

## Registration names

New registrations require `name` (1–100 characters after trimming, with a maximum input length of 100), `email`, and `password`. The registration response includes the saved name. Login continues to use email and password.

For a database already initialized with `001_initial_schema.sql`, apply `002_add_user_name.sql` once before running the updated API. Existing accounts have a null name and can still log in. The new column is nullable for those accounts.

```sh
docker exec -i taskmanagement-postgres-1 psql -U task_app -d task_management \
  -v ON_ERROR_STOP=1 --single-transaction --file=- < db/002_add_user_name.sql
```

## Task due dates and priorities

Apply `db/003_add_task_due_date_and_priority.sql` once to an existing database before starting the updated API. Existing tasks receive `priority: "Medium"` and `dueDate: null`.

```sh
docker exec -i taskmanagement-postgres-1 psql -U task_app -d task_management \
  -v ON_ERROR_STOP=1 --single-transaction --file=- < db/003_add_task_due_date_and_priority.sql
```

Task create and update requests accept `dueDate` as a calendar date (`YYYY-MM-DD`) or null, and `priority` as exactly `Low`, `Medium`, or `High`. Omitting these fields uses no due date and Medium priority, including on PUT. Invalid dates or priorities return 400. Dates have no time or timezone, and dates in the past are allowed.

```json
{
  "title": "Prepare project demo",
  "description": "Review the API flow",
  "dueDate": "2026-12-01",
  "priority": "High"
}
```

Include `isCompleted` when using PUT. Responses from create, list, fetch, and update include both new fields. The frontend provides optional due-date and priority inputs for task creation and editing. Clearing the date removes it. Task rows show the priority and date, and completion/reopen actions preserve both. Dates are displayed as YYYY-MM-DD without timezone conversion.

## Search, filters, and task pages

Apply `db/004_add_task_list_index.sql` once on an existing database. It adds an index on owner, creation time, and ID to support the default list ordering.

`GET /api/tasks` accepts `search`, `isCompleted`, `priority`, `page`, and `pageSize`. Search matches a case-insensitive literal substring in title or description. Surrounding whitespace is trimmed, and an empty search applies no search filter. Filters combine with AND and always restrict results to the signed-in account.

Use `priority=Low`, `Medium`, or `High`, and `isCompleted=true` or `false`. Page defaults to 1 and must be positive. Page size defaults to 20 and must be 1–100. Invalid parameters return 400. Pages beyond the result set return an empty items array and the total matching count.

Example request: `GET /api/tasks?search=demo&isCompleted=false&priority=High&page=1&pageSize=10`

The response is `{ "items": [], "page": 1, "pageSize": 10, "totalCount": 0 }`. Results are ordered by creation time descending, then ID descending. The count and items are read from the same database snapshot.

The frontend provides an explicit Search button, status and priority filters, and Previous/Next controls with 20 items per page. Search and filter changes reset to page 1. Successful task mutations reload the current page; if deletion empties the last page, the list moves back to the last available page. Superseded list requests are canceled, and failed loads have a Retry button.

## Run the integration tests

Open Docker Desktop, then run from the repository root.

```sh
dotnet test tests/TaskManagement.Api.IntegrationTests/TaskManagement.Api.IntegrationTests.csproj
```

The suite starts a disposable PostgreSQL 17 container, applies the SQL scripts in numeric order, and hosts the API in the test process with WebApplicationFactory. Each test creates unique accounts and data. The collection shares one test database, which is removed after the run. The tests generate their own database password and signing keys and use the Testing environment.

To run a single area, add a filter such as `--filter FullyQualifiedName~OwnershipTests`.

| Test file | Behavior |
| --- | --- |
| HealthTests | Database health |
| RegistrationTests | Saved user details, password hashing, duplicates, and validation |
| LoginTests | Credentials, issued token, saved name, and user identity |
| AuthenticationTests | Missing, expired, and tampered bearer tokens |
| TaskCrudTests | Create, Location header, fetch, update, deletion, and missing tasks |
| TaskValidationTests | Invalid input, defaults, date round-trips, and boundary lengths |
| OwnershipTests | Two-account list and access restrictions |
| SearchAndFilterTests | Case-insensitive literal search and combined filters |
| PaginationTests | Defaults, stable ordering, totals, boundaries, and invalid queries |

Start review with RegistrationTests, then follow IntegrationTest and TestApiFactory to see how requests and the API host are created. DatabaseFixture manages the PostgreSQL lifecycle and migrations.

## Continuous integration

The workflow in `.github/workflows/ci.yml` runs on pushes to `main`, pull requests targeting `main`, and manual dispatches. Three jobs check the project on Ubuntu 24.04.

- **API build and integration tests** installs .NET 10, builds the API and tests in Release, and runs the PostgreSQL integration suite. Testcontainers starts its own database using the runner's Docker daemon.
- **Frontend lint and build** installs Node.js 22, restores dependencies with `npm ci`, and runs lint and the production build.
- **API Docker image** builds the Dockerfile.

Test reports are uploaded as `integration-test-results` for seven days, including on test failure when a report was produced. A failing command fails its job. The workflow uses read-only repository access. Test credentials and signing keys are generated by the test fixture.

To run the same checks locally with Docker running:

```sh
dotnet restore tests/TaskManagement.Api.IntegrationTests/TaskManagement.Api.IntegrationTests.csproj
dotnet build tests/TaskManagement.Api.IntegrationTests/TaskManagement.Api.IntegrationTests.csproj --configuration Release --no-restore
dotnet test tests/TaskManagement.Api.IntegrationTests/TaskManagement.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=integration-tests.trx" --results-directory TestResults
npm --prefix frontend ci
npm --prefix frontend run lint
npm --prefix frontend run build
docker build --tag task-management-api:ci .
```

After pushing the repository, open its **Actions** tab and select **CI** to inspect the jobs, step logs, and test report artifact. For a pull request, inspect the checks on the pull request page.
