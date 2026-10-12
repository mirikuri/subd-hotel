function initFilter({ formId, tbodyId, searchUrl }) {
    const form = document.getElementById(formId);
    const tbody = document.getElementById(tbodyId);
    const resetBtn = document.getElementById(formId + '-reset');
    let timer = null;

    function runSearch() {
        const params = new URLSearchParams();
        params.set('handler', 'Search');
        new FormData(form).forEach(function(v, k) { if (v !== '') params.set(k, v); });

        tbody.innerHTML = '<tr><td colspan="99" class="text-center text-muted py-3">Загрузка…</td></tr>';

        fetch(searchUrl + '?' + params.toString(), {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
        .then(function(r) { return r.text(); })
        .then(function(html) { tbody.innerHTML = html; })
        .catch(function() {
            tbody.innerHTML = '<tr><td colspan="99" class="text-center text-danger py-3">Ошибка загрузки</td></tr>';
        });
    }

    form.querySelectorAll('input[type=text]').forEach(function(input) {
        input.addEventListener('input', function() {
            clearTimeout(timer);
            timer = setTimeout(runSearch, 250);
        });
    });

    form.querySelectorAll('select').forEach(function(sel) {
        sel.addEventListener('change', runSearch);
    });

    resetBtn.addEventListener('click', function() {
        form.reset();
        runSearch();
    });
}
