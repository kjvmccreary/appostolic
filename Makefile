# Load environment variables from .env if present
-include .env
export $(shell sed 's/=.*//' .env)

DC := docker compose --env-file .env -f infra/docker/compose.yml
PGPORT ?= 55432

nuke:
	$(DC) down -v --remove-orphans || true
	rm -rf infra/docker/data/postgres
	mkdir -p infra/docker/data/postgres

up: ; $(DC) up -d

down:
	$(DC) down --remove-orphans

wait-postgres:
	@echo "Waiting for Postgres to become healthy..."
	@CID="$$( $(DC) ps -q postgres )"; \
	until [ "$$(docker inspect -f '{{.State.Health.Status}}' $$CID)" = "healthy" ]; do sleep 1; done; \
	echo "Postgres is healthy."

migrate:
	dotnet tool restore
	cd apps/api && dotnet ef database update

seed:
	@echo "Seeding demo data..."
	dotnet build apps/api/Appostolic.Api.csproj
	cd apps/api/tools/seed && \
	  PGHOST=localhost PGPORT=$(PGPORT) PGDATABASE=$${POSTGRES_DB} PGUSER=$${POSTGRES_USER} PGPASSWORD=$${POSTGRES_PASSWORD} dotnet run

bootstrap: nuke up wait-postgres migrate seed
	@echo "✅ Bootstrap complete. Next: make api | make web | make mobile"

doctor:
	./scripts/dev-doctor.sh

api:
	./scripts/kill-port.sh 5198 || true
	cd apps/api && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5198 dotnet watch

# Local HTTPS for API (Story 5a). Requires dev cert: run `dotnet dev-certs https --trust` once.
api-https:
	./scripts/kill-port.sh 5198 || true
	cd apps/api && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=https://localhost:5198 dotnet watch

web:
	cd apps/web && pnpm dev

mobile:
	cd apps/mobile && pnpm dev -- --port 8082

# Run notifications worker (dispatcher outside API)
worker:
	dotnet run --project apps/notifications-worker/Appostolic.Notifications.Worker.csproj


mobile-ios:
	cd apps/mobile && pnpm dev -- --port 8082 --open --ios

clean-dotnet:
	find . -name bin -o -name obj -type d -prune -exec rm -rf {} +
	dotnet nuget locals all --clear

sdk:
	dotnet build apps/api/Appostolic.Api.csproj
	pnpm run sdk:gen

dev:
	# run API + Web concurrently; leave mobile manual (needs interactive Expo)
	( cd apps/api && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5198 dotnet watch ) & \
	( cd apps/web && pnpm dev )

test:
	# Run .NET API tests with coverage (Coverlet) and Web Vitest with coverage
	dotnet test apps/api.tests/Appostolic.Api.Tests.csproj \
	  -p:CollectCoverage=true \
	  -p:CoverletOutputFormat=cobertura \
	  -p:CoverletOutput=./TestResults/coverage.cobertura.xml \
	  --logger "trx;LogFileName=TestResults.trx"
	cd apps/web && pnpm test -- --coverage

fetest:
	pnpm --filter @appostolic/web test