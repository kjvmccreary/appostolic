# Load environment variables from .env if present
-include .env
export $(shell sed 's/=.*//' .env 2>/dev/null)

.PHONY: up down logs psql api web mobile mobile-ios

up:
	docker compose -f infra/docker/compose.yml up -d
	@echo "\n🚀 Infra is up!"
	@echo "Postgres → host=localhost port=$(POSTGRES_PORT) user=$(POSTGRES_USER) password=$(POSTGRES_PASSWORD) db=$(POSTGRES_DB)"
	@echo "pgAdmin  → http://localhost:8081 (login: $(PGADMIN_EMAIL) / $(PGADMIN_PASSWORD))"
	@echo "Mailhog  → http://localhost:8025"
	@echo "MinIO    → http://localhost:9003 (user: $(MINIO_ROOT_USER), pass: $(MINIO_ROOT_PASSWORD))"

down:
	docker compose -f infra/docker/compose.yml down -v

logs:
	docker compose -f infra/docker/compose.yml logs -f

psql:
	PGPASSWORD=$$POSTGRES_PASSWORD psql -h localhost -p 55432 -U $$POSTGRES_USER $$POSTGRES_DB

api:
	cd apps/api && ASPNETCORE_URLS=http://localhost:5198 dotnet watch

web:
	cd apps/web && pnpm dev

mobile:
	cd apps/mobile && pnpm dev -- --port 8082

mobile-ios:
	cd apps/mobile && pnpm dev -- --port 8082 --open --ios
