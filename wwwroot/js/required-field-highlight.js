// Global "compulsory field missing" highlighter for the whole CRM.
// Applies to every <form> on every page automatically — no per-view wiring needed.
// When a required input/select/textarea is left empty on submit, this:
//   1) Highlights the field in red (adds Bootstrap's .is-invalid class)
//   2) Shows a clear inline message under the field ("<Label> is required.")
//   3) Stops the form from submitting and focuses/scrolls to the first missing field
//   4) Shows a short summary banner at the top of the form
(function () {
    'use strict';

    function isVisible(el) {
        return !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length);
    }

    function findLabelText(field) {
        // 1) <label for="id">
        if (field.id) {
            var byFor = document.querySelector('label[for="' + CSS.escape(field.id) + '"]');
            if (byFor && byFor.textContent.trim()) return byFor.textContent.replace(/\*/g, '').trim();
        }
        // 2) closest wrapping <label>
        var wrappingLabel = field.closest('label');
        if (wrappingLabel && wrappingLabel.textContent.trim()) return wrappingLabel.textContent.replace(/\*/g, '').trim();
        // 3) a sibling/previous label inside the same field group
        var group = field.closest('.col-md-1,.col-md-2,.col-md-3,.col-md-4,.col-md-6,.col-md-8,.col-md-12,.form-group,.mb-3,.form-span-2,div');
        if (group) {
            var lbl = group.querySelector('label.form-label, label');
            if (lbl && lbl.textContent.trim()) return lbl.textContent.replace(/\*/g, '').trim();
        }
        // 4) placeholder / aria-label / name as a last resort
        return (field.getAttribute('placeholder') || field.getAttribute('aria-label') || field.name || 'This field').trim();
    }

    function isRequiredField(field) {
        if (field.disabled || field.type === 'hidden') return false;
        if (field.type === 'checkbox' || field.type === 'radio') {
            // FIX (2026-08-18): ASP.NET Core's tag helpers automatically add
            // data-val-required to EVERY non-nullable bool property (e.g.
            // IsPaidTraining, CustomerContacted, ManualProvided) even when the
            // developer never marked it [Required] - it's just how implicit
            // model validation works for value types. For a checkbox, "required"
            // is interpreted as "must be checked", so that implicit attribute
            // was silently forcing optional toggle switches (like "Paid
            // training") to be turned ON before the form could be submitted -
            // reported as the "Paid Training" switch being hardcoded/forced.
            // Only an explicit `required` attribute (a real must-agree checkbox)
            // should ever make a checkbox mandatory; data-val-required must be
            // ignored for checkboxes/radios.
            return field.hasAttribute('required');
        }
        return field.hasAttribute('required') || field.getAttribute('data-val-required') !== null;
    }

    function isEmpty(field) {
        if (field.type === 'checkbox' || field.type === 'radio') return !field.checked;
        return !field.value || !field.value.toString().trim();
    }

    function ensureMessage(field, text) {
        // Reuse an existing ASP.NET validation span (span[data-valmsg-for]) if present,
        // otherwise create/reuse a small message element right after the field.
        var msg = null;
        if (field.id) {
            msg = document.querySelector('[data-valmsg-for="' + CSS.escape(field.name || '') + '"]');
        }
        if (!msg) {
            msg = field.parentElement ? field.parentElement.querySelector('.field-missing-msg[data-for="' + (field.name || '') + '"]') : null;
        }
        if (!msg) {
            msg = document.createElement('div');
            msg.className = 'field-missing-msg text-danger small mt-1';
            msg.setAttribute('data-for', field.name || '');
            field.insertAdjacentElement('afterend', msg);
        } else {
            msg.classList.add('field-missing-msg', 'text-danger', 'small', 'mt-1');
        }
        msg.textContent = text;
        msg.style.display = '';
    }

    function clearFieldState(field) {
        field.classList.remove('is-invalid');
        var msg = field.parentElement ? field.parentElement.querySelector('.field-missing-msg[data-for="' + (field.name || '') + '"]') : null;
        if (!msg && field.nextElementSibling && field.nextElementSibling.classList && field.nextElementSibling.classList.contains('field-missing-msg')) {
            msg = field.nextElementSibling;
        }
        if (msg) msg.textContent = '';
    }

    function markInvalid(field) {
        field.classList.add('is-invalid');
        var label = findLabelText(field);
        ensureMessage(field, label + ' is required.');
    }

    function removeSummary(form) {
        var existing = form.querySelector(':scope > .missing-field-summary');
        if (existing) existing.remove();
    }

    function showSummary(form, count) {
        removeSummary(form);
        var box = document.createElement('div');
        box.className = 'alert alert-danger missing-field-summary d-flex align-items-center gap-2 mb-3';
        box.innerHTML = '<i class="bi bi-exclamation-triangle-fill"></i><span>' +
            (count === 1 ? 'Please fill the highlighted required field.' : 'Please fill the ' + count + ' highlighted required fields.') +
            '</span>';
        form.insertBefore(box, form.firstChild);
    }

    function validateForm(form) {
        var fields = form.querySelectorAll('input, select, textarea');
        var firstInvalid = null;
        var invalidCount = 0;
        fields.forEach(function (field) {
            if (!isRequiredField(field)) return;
            if (!isVisible(field)) return; // skip fields hidden by conditional UI
            if (isEmpty(field)) {
                markInvalid(field);
                invalidCount++;
                if (!firstInvalid) firstInvalid = field;
            } else {
                clearFieldState(field);
            }
        });

        if (invalidCount > 0) {
            showSummary(form, invalidCount);
            if (firstInvalid) {
                firstInvalid.focus({ preventScroll: true });
                firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
            return false;
        }

        removeSummary(form);
        return true;
    }

    function wireForm(form) {
        if (form.dataset.requiredHighlightWired === '1') return;
        form.dataset.requiredHighlightWired = '1';

        // Take over from native browser validation so we control the highlight + message.
        if (!form.hasAttribute('data-native-validate')) {
            form.setAttribute('novalidate', 'novalidate');
        }

        form.addEventListener('submit', function (event) {
            var ok = validateForm(form);
            if (!ok) {
                event.preventDefault();
                event.stopPropagation();
            }
        }, true);

        form.addEventListener('input', function (event) {
            var field = event.target;
            if (field && isRequiredField(field) && !isEmpty(field)) clearFieldState(field);
        });
        form.addEventListener('change', function (event) {
            var field = event.target;
            if (field && isRequiredField(field) && !isEmpty(field)) clearFieldState(field);
        });
    }

    function wireAllForms() {
        document.querySelectorAll('form').forEach(wireForm);
    }

    document.addEventListener('DOMContentLoaded', wireAllForms);

    // Re-scan when new content (modals, collapsed panels, AJAX-loaded forms) appears.
    var observer = new MutationObserver(function (mutations) {
        var shouldScan = false;
        mutations.forEach(function (m) {
            if (m.addedNodes && m.addedNodes.length) shouldScan = true;
        });
        if (shouldScan) wireAllForms();
    });
    document.addEventListener('DOMContentLoaded', function () {
        observer.observe(document.body, { childList: true, subtree: true });
    });
})();
