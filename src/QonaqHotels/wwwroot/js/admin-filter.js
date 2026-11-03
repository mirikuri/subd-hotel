function initFilter({ formId, tbodyId, searchUrl }) {
    const form = document.getElementById(formId);
    const tbody = document.getElementById(tbodyId);
    const resetBtn = document.getElementById(formId + '-reset');

    if (!form || !tbody || !resetBtn) return;

    // Сброс дочернего select
    function resetSelect(select) {
        select.dataset.requestId =
            String(Number(select.dataset.requestId || 0) + 1);

        select.replaceChildren(
            new Option(select.dataset.placeholder, '')
        );

        select.value = '';
        select.disabled = true;
    }

    // Загрузка вариантов зависимого select
    function loadOptions(select, parentValue) {
        const requestId =
            Number(select.dataset.requestId || 0) + 1;

        select.dataset.requestId = String(requestId);

        const url = new URL(searchUrl, window.location.origin);

        url.searchParams.set(
            'handler',
            select.dataset.optionsHandler
        );

        url.searchParams.set(
            select.dataset.optionsParameter,
            parentValue
        );

        fetch(url.toString(), {
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
        .then(response => {
            if (!response.ok) {
                throw new Error('Ошибка загрузки');
            }

            return response.json();
        })
        .then(options => {
            // Игнорируем устаревшие ответы
            if (Number(select.dataset.requestId) !== requestId) {
                return;
            }

            options.forEach(item => {
                select.add(
                    new Option(item.label, item.value)
                );
            });

            select.disabled = false;
        })
        .catch(error => {
            if (Number(select.dataset.requestId) !== requestId) {
                return;
            }

            console.error(error);
            select.disabled = true;
        });
    }

    // Обновление всех дочерних фильтров
    function updateChildren(parentName) {
        const parent = form.querySelector(
            `[name="${parentName}"]`
        );

        if (!parent) return;

        const parentValue = parent.value;

        const children = form.querySelectorAll(
            'select[data-depends-on]'
        );

        children.forEach(child => {
            if (child.dataset.dependsOn !== parentName) {
                return;
            }

            // Сбрасываем потомков
            resetSelect(child);
            updateChildren(child.name);

            if (parentValue) {
                loadOptions(child, parentValue);
            }
        });
    }

    // Реакция на изменение любого select
    form.querySelectorAll('select').forEach(select => {
        select.addEventListener('change', function () {
            updateChildren(this.name);
        });
    });

    // Поиск
    function runSearch() {
        const params = new URLSearchParams();

        params.set('handler', 'Search');

        new FormData(form).forEach(function (value, key) {
            if (value !== '') {
                params.set(key, value);
            }
        });

        tbody.innerHTML =
            '<tr><td colspan="99" class="text-center text-muted py-3">Загрузка…</td></tr>';

        fetch(searchUrl + '?' + params.toString(), {
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
        .then(response => response.text())
        .then(html => {
            tbody.innerHTML = html;
        })
        .catch(() => {
            tbody.innerHTML =
                '<tr><td colspan="99" class="text-center text-danger py-3">Ошибка загрузки</td></tr>';
        });
    }

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        runSearch();
    });

    // Сброс фильтров
    resetBtn.addEventListener('click', function () {
        form.reset();

        form.querySelectorAll(
            'select[data-depends-on]'
        ).forEach(resetSelect);

        runSearch();
    });
}