(function (exports) {
    function showError(input, msg) {
        let span = input.nextElementSibling;
        if (!span || !span.classList.contains('field-error')) {
            span = document.createElement('span');
            span.className = 'field-error text-danger small';
            input.after(span);
        }
        span.textContent = msg;
        input.classList.add('is-invalid');
    }

    function clearError(input) {
        const span = input.nextElementSibling;
        if (span && span.classList.contains('field-error')) span.textContent = '';
        input.classList.remove('is-invalid');
    }

    function validatePhone(input) {
        const v = input.value.trim();
        if (v === '') { clearError(input); return true; }
        if (/^\+[0-9]{7,15}$/.test(v)) { clearError(input); return true; }
        showError(input, 'Формат: +XXXXXXXXXXX (7–15 цифр после +)');
        return false;
    }

    function validateName(input) {
        const v = input.value.trim();
        if (v.length >= 2) { clearError(input); return true; }
        showError(input, 'Минимум 2 символа');
        return false;
    }

    function validateDateRange(startInput, endInput, allowPastStart) {
        const today = new Date().toISOString().split('T')[0];
        if (!allowPastStart) startInput.min = today;

        function revalidate() {
            const s = startInput.value;
            const e = endInput.value;
            if (s) {
                const minEnd = new Date(s);
                minEnd.setDate(minEnd.getDate() + 1);
                endInput.min = minEnd.toISOString().split('T')[0];
            }
            if (s && e && e <= s) {
                showError(endInput, 'Дата выезда должна быть позже даты заезда');
                return false;
            }
            clearError(endInput);
            return true;
        }

        startInput.addEventListener('change', revalidate);
        endInput.addEventListener('change', revalidate);
        return revalidate;
    }

    function validateDismissalDate(hireDateInput, dismissalInput) {
        function revalidate() {
            const today = new Date().toISOString().split('T')[0];
            dismissalInput.max = today;
            const h = hireDateInput.value;
            if (h) dismissalInput.min = h;
            const d = dismissalInput.value;
            if (d && h && d < h) {
                showError(dismissalInput, 'Дата увольнения не может быть раньше даты приёма');
                return false;
            }
            if (d && d > today) {
                showError(dismissalInput, 'Нельзя указать дату увольнения в будущем');
                return false;
            }
            clearError(dismissalInput);
            return true;
        }

        hireDateInput.addEventListener('change', revalidate);
        dismissalInput.addEventListener('change', revalidate);
        return revalidate;
    }

    function bindSubmitGate(form, validators) {
        const btn = form.querySelector('[type=submit]');
        function check() {
            const ok = validators.every(function(v) { return v(); });
            btn.disabled = !ok;
        }
        form.querySelectorAll('input, select, textarea').forEach(function(el) {
            el.addEventListener('input', check);
            el.addEventListener('change', check);
        });
        check();
    }

    exports.adminValidation = { validatePhone, validateName, validateDateRange, validateDismissalDate, bindSubmitGate };
})(window);
