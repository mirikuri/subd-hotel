'use strict';

// Выпадающие списки с поиском.
//
// Каждый <select> (кроме multiple и помеченных data-no-search) получает поле ввода:
// клик открывает список, ввод текста фильтрует варианты, стрелки + Enter выбирают.
// Родной <select> остаётся в DOM (скрыт) и по-прежнему хранит значение — формы,
// валидация и скрипты страниц, которые читают select.value, подписываются на change
// или перезаполняют options, работают без изменений.
(function () {
    const MAX_HEIGHT = 280;
    let uid = 0;

    // Поиск без учёта регистра и разницы «ё/е».
    const norm = s => s.toLowerCase().replace(/ё/g, 'е');

    // Начала слов в тексте: «Иванов Иван (+7701…)» → позиции «И», «И», «7».
    function wordStarts(text) {
        const starts = [];
        for (let i = 0; i < text.length; i++) {
            const isWord = /[\p{L}\p{N}]/u.test(text[i]);
            const prevWord = i > 0 && /[\p{L}\p{N}]/u.test(text[i - 1]);
            if (isWord && !prevWord) starts.push(i);
        }
        return starts;
    }

    // Каждое слово запроса должно совпасть с началом какого-то слова варианта
    // («ив ив» найдёт «Иванов Иван», «алм» — «Qonaq Алматы»).
    // Возвращает отрезки для подсветки или null, если вариант не подходит.
    function match(text, query) {
        const t = norm(text);
        const starts = wordStarts(text);
        const used = new Set();
        const ranges = [];
        for (const term of norm(query).split(/\s+/).filter(Boolean)) {
            const pos = starts.find(s => !used.has(s) && t.startsWith(term, s));
            if (pos === undefined) return null;
            used.add(pos);
            ranges.push([pos, pos + term.length]);
        }
        return ranges.sort((a, b) => a[0] - b[0]);
    }

    // Текст варианта с выделенными совпадениями (через узлы, без innerHTML).
    function highlight(li, text, ranges) {
        let last = 0;
        for (const [from, to] of ranges) {
            if (from < last) continue;
            li.append(text.slice(last, from));
            const mark = document.createElement('mark');
            mark.textContent = text.slice(from, to);
            li.append(mark);
            last = to;
        }
        li.append(text.slice(last));
    }

    const proto = HTMLSelectElement.prototype;
    const valueDesc = Object.getOwnPropertyDescriptor(proto, 'value');
    const indexDesc = Object.getOwnPropertyDescriptor(proto, 'selectedIndex');

    function enhance(select) {
        if (select.multiple || select.dataset.noSearch !== undefined || select.dataset.ssReady) return;
        select.dataset.ssReady = '1';

        const id = 'ss-' + (++uid);

        // Ширину берём у исходного select: полноширинные остаются полноширинными,
        // компактные (фильтры, шахматка) не схлопываются.
        const parentWidth = select.parentElement ? select.parentElement.clientWidth : 0;
        const ownWidth = select.getBoundingClientRect().width;

        const wrap = document.createElement('div');
        wrap.className = 'ss';
        if (ownWidth > 0 && parentWidth > 0 && ownWidth < parentWidth - 4) {
            wrap.classList.add('ss-inline');
            wrap.style.minWidth = Math.ceil(ownWidth) + 'px';
        }

        const input = document.createElement('input');
        input.type = 'text';
        input.autocomplete = 'off';
        input.spellcheck = false;
        input.className = 'ss-input ' + select.className;
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-controls', id + '-list');

        // Подпись <label for="..."> переводим на поле ввода.
        if (select.id) {
            input.id = select.id + '-search';
            document.querySelectorAll('label[for="' + CSS.escape(select.id) + '"]')
                .forEach(l => { l.htmlFor = input.id; });
        }
        if (select.getAttribute('aria-label')) input.setAttribute('aria-label', select.getAttribute('aria-label'));

        const list = document.createElement('ul');
        list.className = 'ss-list';
        list.id = id + '-list';
        list.setAttribute('role', 'listbox');
        list.hidden = true;

        select.before(wrap);
        wrap.append(select, input);
        document.body.append(list); // в body — чтобы overflow:hidden родителей не обрезал список
        select.classList.add('ss-native');
        select.tabIndex = -1;
        select.setAttribute('aria-hidden', 'true');

        let items = [];     // [{ option, li }] — видимые после фильтра
        let active = -1;
        let open = false;

        const selectedOption = () => select.options[select.selectedIndex] || null;

        // Отображение выбранного значения в поле.
        function syncDisplay() {
            const opt = selectedOption();
            const isEmpty = !opt || opt.value === '';
            // Пустой вариант («— выберите —», «Все города») становится плейсхолдером.
            const emptyOpt = Array.from(select.options).find(o => o.value === '');
            input.placeholder = emptyOpt ? emptyOpt.text : '';
            if (!open) input.value = isEmpty ? '' : opt.text;
            input.disabled = select.disabled;
            input.classList.toggle('is-invalid', select.classList.contains('is-invalid'));
            if (select.disabled && open) close(false);
        }

        function render(query) {
            const q = query.trim();
            list.replaceChildren();
            items = [];
            const current = selectedOption();

            Array.from(select.options).forEach(option => {
                if (option.hidden) return;
                // При поиске пустой вариант («— выберите —») не нужен.
                if (q && option.value === '') return;
                const ranges = q ? match(option.text, q) : [];
                if (!ranges) return;

                const li = document.createElement('li');
                li.className = 'ss-option';
                li.id = id + '-opt-' + items.length;
                li.setAttribute('role', 'option');
                highlight(li, option.text, ranges);
                if (option.value === '') li.classList.add('ss-empty');
                if (option === current) {
                    li.classList.add('ss-selected');
                    li.setAttribute('aria-selected', 'true');
                }
                if (option.disabled) {
                    li.classList.add('ss-disabled');
                    li.setAttribute('aria-disabled', 'true');
                }
                list.append(li);
                items.push({ option, li });
            });

            if (items.length === 0) {
                const li = document.createElement('li');
                li.className = 'ss-none';
                li.textContent = 'Ничего не найдено';
                list.append(li);
            }

            // Подсветка: выбранный вариант при открытии, первый подходящий при поиске.
            let start = q ? -1 : items.findIndex(it => it.option === current);
            if (start < 0) start = items.findIndex(it => !it.option.disabled);
            setActive(start);
        }

        function setActive(i) {
            if (active >= 0 && items[active]) items[active].li.classList.remove('ss-active');
            active = i;
            if (i >= 0 && items[i]) {
                items[i].li.classList.add('ss-active');
                input.setAttribute('aria-activedescendant', items[i].li.id);
                const li = items[i].li;
                if (li.offsetTop < list.scrollTop) list.scrollTop = li.offsetTop;
                else if (li.offsetTop + li.offsetHeight > list.scrollTop + list.clientHeight)
                    list.scrollTop = li.offsetTop + li.offsetHeight - list.clientHeight;
            } else {
                input.removeAttribute('aria-activedescendant');
            }
        }

        function move(step) {
            if (!items.length) return;
            let i = active;
            for (let n = 0; n < items.length; n++) {
                i = (i + step + items.length) % items.length;
                if (!items[i].option.disabled) { setActive(i); return; }
            }
        }

        function position() {
            const r = input.getBoundingClientRect();
            const below = window.innerHeight - r.bottom;
            const up = below < Math.min(MAX_HEIGHT, list.scrollHeight) + 8 && r.top > below;
            list.style.left = r.left + 'px';
            list.style.minWidth = r.width + 'px';
            list.style.maxWidth = Math.max(r.width, window.innerWidth - r.left - 8) + 'px';
            list.style.maxHeight = Math.min(MAX_HEIGHT, (up ? r.top : below) - 8) + 'px';
            if (up) {
                list.style.top = '';
                list.style.bottom = (window.innerHeight - r.top + 2) + 'px';
            } else {
                list.style.bottom = '';
                list.style.top = (r.bottom + 2) + 'px';
            }
        }

        function openList() {
            if (open || select.disabled) return;
            open = true;
            wrap.classList.add('ss-open');
            input.setAttribute('aria-expanded', 'true');
            list.hidden = false;
            // Поле очищается под ввод запроса, текущее значение видно как подсказка.
            const opt = selectedOption();
            if (opt && opt.value !== '') input.placeholder = opt.text;
            input.value = '';
            render('');
            position();
        }

        function close(restore = true) {
            if (!open) return;
            open = false;
            wrap.classList.remove('ss-open');
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            list.hidden = true;
            if (restore) syncDisplay();
        }

        function choose(i) {
            const it = items[i];
            if (!it || it.option.disabled) return;
            const changed = select.value !== it.option.value || select.selectedIndex !== it.option.index;
            select.selectedIndex = it.option.index;
            close();
            if (changed) {
                select.dispatchEvent(new Event('input', { bubbles: true }));
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }

        input.addEventListener('mousedown', () => {
            if (document.activeElement === input) open ? close() : openList();
        });
        input.addEventListener('focus', openList);
        input.addEventListener('input', () => {
            if (!open) openList();
            render(input.value);
            position();
        });
        input.addEventListener('keydown', e => {
            switch (e.key) {
                case 'ArrowDown':
                    e.preventDefault();
                    open ? move(1) : openList();
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    open ? move(-1) : openList();
                    break;
                case 'Enter':
                    // Enter в открытом списке выбирает вариант, а не отправляет форму.
                    if (open) { e.preventDefault(); choose(active); }
                    break;
                case 'Escape':
                    if (open) { e.preventDefault(); close(); }
                    break;
                case 'Tab':
                    close();
                    break;
            }
        });
        input.addEventListener('blur', () => close());

        // mousedown, а не click: иначе blur поля закроет список раньше выбора.
        list.addEventListener('mousedown', e => {
            e.preventDefault();
            const li = e.target.closest('.ss-option');
            if (!li) return;
            choose(items.findIndex(it => it.li === li));
        });

        // Если кто-то сфокусирует сам select (скрипт, label) — передаём фокус полю.
        select.addEventListener('focus', () => input.focus());
        select.addEventListener('change', syncDisplay);

        // Скрипты страниц меняют select программно (value, selectedIndex) — без события change.
        Object.defineProperty(select, 'value', {
            configurable: true,
            get() { return valueDesc.get.call(this); },
            set(v) { valueDesc.set.call(this, v); syncDisplay(); }
        });
        Object.defineProperty(select, 'selectedIndex', {
            configurable: true,
            get() { return indexDesc.get.call(this); },
            set(v) { indexDesc.set.call(this, v); syncDisplay(); }
        });

        // Перезаполнение options, disabled, класс is-invalid.
        new MutationObserver(() => {
            syncDisplay();
            if (open) { render(input.value); position(); }
        }).observe(select, {
            childList: true, subtree: true, characterData: true,
            attributes: true, attributeFilter: ['disabled', 'class', 'selected', 'hidden', 'label']
        });

        // form.reset() не шлёт change.
        if (select.form) select.form.addEventListener('reset', () => setTimeout(syncDisplay));

        window.addEventListener('resize', () => open && position());
        window.addEventListener('scroll', e => {
            if (open && e.target !== list) position();
        }, true);

        syncDisplay();
    }

    function init(root) {
        (root || document).querySelectorAll('select').forEach(enhance);
    }

    window.searchableSelect = { init };
    init();
})();
