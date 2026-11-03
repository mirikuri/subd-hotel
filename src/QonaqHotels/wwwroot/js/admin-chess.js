(function () {
    var form      = document.getElementById('chess-filter');
    var grid      = document.getElementById('chess-grid');
    var dateIn    = document.getElementById('chess-date-from');
    var label     = document.getElementById('chess-period-label');
    var bar       = document.getElementById('chess-action-bar');
    var barLabel  = document.getElementById('chess-sel-label');
    var barCreate = document.getElementById('chess-sel-create');

    var MONTHS = ['янв','фев','мар','апр','май','июн','июл','авг','сен','окт','ноя','дек'];

    // ── Helpers ───────────────────────────────────────────
    function fmtPeriod(isoDate) {
        var f = new Date(isoDate + 'T00:00:00');
        var t = new Date(isoDate + 'T00:00:00');
        t.setDate(t.getDate() + 13);
        if (f.getMonth() === t.getMonth() && f.getFullYear() === t.getFullYear())
            return f.getDate() + ' – ' + t.getDate() + ' ' + MONTHS[t.getMonth()] + ' ' + t.getFullYear();
        return f.getDate() + ' ' + MONTHS[f.getMonth()] + ' – ' +
               t.getDate() + ' ' + MONTHS[t.getMonth()] + ' ' + t.getFullYear();
    }

    function fmtDay(iso) {
        var d = new Date(iso + 'T00:00:00');
        return d.getDate() + ' ' + MONTHS[d.getMonth()];
    }

    function addDays(iso, n) {
        var d = new Date(iso + 'T00:00:00');
        d.setDate(d.getDate() + n);
        return d.toISOString().slice(0, 10);
    }

    // ── Grid reload ───────────────────────────────────────
    function reloadGrid() {
        clearSel();
        var params = new URLSearchParams();
        params.set('handler', 'Grid');
        params.set('hotelId', form.querySelector('[name=hotelId]').value);
        params.set('dateFrom', dateIn.value);
        form.querySelectorAll('input[name=statuses]:checked').forEach(function (cb) {
            params.append('statuses', cb.value);
        });
        label.textContent = fmtPeriod(dateIn.value);
        grid.style.opacity = '0.4';
        fetch('/Admin/Chess?' + params.toString(), {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (r) { return r.text(); })
            .then(function (html) { grid.innerHTML = html; grid.style.opacity = '1'; })
            .catch(function () { grid.style.opacity = '1'; });
    }

    function shiftDate(days) {
        var d = new Date(dateIn.value + 'T00:00:00');
        d.setDate(d.getDate() + days);
        dateIn.value = d.toISOString().slice(0, 10);
        reloadGrid();
    }

    // ── Selection state ───────────────────────────────────
    var sel = { roomId: null, roomNum: null, dateFrom: null, dateTo: null };

    function highlightSel() {
        grid.querySelectorAll('.chess-sel-start, .chess-sel-range').forEach(function (td) {
            td.classList.remove('chess-sel-start', 'chess-sel-range');
        });
        if (!sel.roomId) return;
        grid.querySelectorAll('.chess-empty[data-room-id="' + sel.roomId + '"]').forEach(function (td) {
            var d = td.dataset.date;
            if (!sel.dateTo) {
                if (d === sel.dateFrom) td.classList.add('chess-sel-start');
            } else {
                if (d >= sel.dateFrom && d <= sel.dateTo)
                    td.classList.add(d === sel.dateFrom ? 'chess-sel-start' : 'chess-sel-range');
            }
        });
    }

    function showBar() {
        if (!sel.dateTo) {
            barLabel.textContent = 'Комната ' + sel.roomNum + ' · выберите конец периода';
            barCreate.style.display = 'none';
        } else {
            barLabel.textContent = 'Комната ' + sel.roomNum + ' · ' +
                fmtDay(sel.dateFrom) + ' – ' + fmtDay(sel.dateTo);
            var hotelId = form.querySelector('[name=hotelId]').value;
            barCreate.href = '/Admin/Bookings/Create?roomId=' + sel.roomId +
                '&hotelId=' + hotelId +
                '&dateFrom=' + sel.dateFrom +
                '&dateTo=' + addDays(sel.dateTo, 1);
            barCreate.style.display = '';
        }
        bar.style.display = '';
    }

    function clearSel() {
        sel = { roomId: null, roomNum: null, dateFrom: null, dateTo: null };
        grid.querySelectorAll('.chess-sel-start, .chess-sel-range').forEach(function (td) {
            td.classList.remove('chess-sel-start', 'chess-sel-range');
        });
        bar.style.display = 'none';
        barCreate.style.display = 'none';
    }

    // ── Click on grid ─────────────────────────────────────
    grid.addEventListener('click', function (e) {
        var td = e.target.closest('.chess-empty');
        if (!td) { clearSel(); return; }

        var roomId  = td.dataset.roomId;
        var roomNum = td.dataset.roomNum;
        var date    = td.dataset.date;

        if (!sel.roomId) {
            sel = { roomId: roomId, roomNum: roomNum, dateFrom: date, dateTo: null };
        } else if (sel.roomId === roomId && !sel.dateTo) {
            var from = sel.dateFrom <= date ? sel.dateFrom : date;
            var to   = sel.dateFrom <= date ? date         : sel.dateFrom;
            sel.dateFrom = from;
            sel.dateTo   = to;
        } else {
            sel = { roomId: roomId, roomNum: roomNum, dateFrom: date, dateTo: null };
        }
        highlightSel();
        showBar();
    });

    document.getElementById('chess-sel-clear').addEventListener('click', clearSel);

    // ── Nav buttons ───────────────────────────────────────
    document.getElementById('chess-prev').addEventListener('click', function () { shiftDate(-14); });
    document.getElementById('chess-next').addEventListener('click', function () { shiftDate(14); });
    document.getElementById('chess-today').addEventListener('click', function () {
        dateIn.value = new Date().toISOString().slice(0, 10);
        reloadGrid();
    });

    form.querySelector('[name=hotelId]').addEventListener('change', reloadGrid);
    form.querySelectorAll('input[name=statuses]').forEach(function (cb) {
        cb.addEventListener('change', reloadGrid);
    });

    label.textContent = fmtPeriod(dateIn.value);
}());
