#!/usr/bin/env bash
# Пересоздаёт базу с нуля: DROP DATABASE → CREATE DATABASE → schema.sql → seed.sql.
#
#   db/reset.sh                 # база gosti (как в appsettings.Development.json), спросит подтверждение
#   db/reset.sh gosti_test      # отдельная база для тестов (tests/QonaqHotels.Tests/TestDb.cs)
#   db/reset.sh -y gosti        # без вопроса
#
# ВСЕ ДАННЫЕ В БАЗЕ УДАЛЯЮТСЯ. Подключение — переменными libpq (PGHOST, PGPORT, PGUSER,
# PGPASSWORD); по умолчанию localhost и пользователь postgres.
# Учётку admin создаст приложение при первом запуске: нужна переменная ADMIN_INITIAL_PASSWORD.
set -euo pipefail

assume_yes=false
if [[ "${1:-}" == "-y" ]]; then
    assume_yes=true
    shift
fi

db="${1:-gosti}"
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export PGHOST="${PGHOST:-localhost}"
export PGUSER="${PGUSER:-postgres}"

if [[ "$assume_yes" == false && -t 0 ]]; then
    read -r -p "Все данные в базе «$db» на $PGHOST будут удалены. Продолжить? [y/N] " answer
    [[ "$answer" == [yYдД]* ]] || { echo "Отменено."; exit 1; }
fi

# --force отключает открытые соединения (например, запущенное приложение)
dropdb --if-exists --force "$db"
createdb "$db"

# --single-transaction: если в файле ошибка, база не останется наполовину заполненной
psql -q -X -v ON_ERROR_STOP=1 --single-transaction -d "$db" -f "$dir/schema.sql"
psql -q -X -v ON_ERROR_STOP=1 --single-transaction -d "$db" -f "$dir/seed.sql"

# Свежая статистика для планировщика (и точные n_live_tup для отчёта ниже)
psql -q -X -d "$db" -c "ANALYZE"

echo "База «$db» пересоздана:"
psql -X -d "$db" -c "
    SELECT relname AS \"таблица\", n_live_tup AS \"строк\"
    FROM pg_stat_user_tables
    ORDER BY relname"
