// Shared "Add Debt Transaction" dialog used by the Customers list and Details pages.
// Direction is chosen with two sign-marked cards instead of a dropdown so it is obvious
// whether the customer's balance goes up (+, customer owes me) or down (−, I got paid).
(function () {
    'use strict';

    function directionCard(value, sign, title, subtitle, checked) {
        return `
            <label class="debt-dir-card debt-dir-${value === 'true' ? 'plus' : 'minus'}">
                <input type="radio" name="debt-direction" value="${value}" ${checked ? 'checked' : ''}>
                <span class="debt-dir-sign">${sign}</span>
                <span class="debt-dir-title">${title}</span>
                <span class="debt-dir-sub">${subtitle}</span>
            </label>`;
    }

    window.openDebtTransactionDialog = function (customerId, url, onSaved) {
        Swal.fire({
            title: 'Add Debt Transaction',
            width: '420px',
            padding: '1rem',
            customClass: { popup: 'swal-compact' },
            html: `
                <div class="debt-dialog">
                    <div class="debt-dir-group mb-3">
                        ${directionCard('true', '+', 'Customer owes me',
                            'Credit sale, or I gave them money', true)}
                        ${directionCard('false', '−', 'I got paid',
                            'Customer paid me, or I owe them', false)}
                    </div>
                    <div class="mb-2">
                        <label class="form-label fw-bold mb-1" for="debt-amount">Amount *</label>
                        <div class="debt-amount-wrap">
                            <span class="debt-amount-sign">+</span>
                            <input type="number" id="debt-amount" class="form-control" step="0.01" min="0.01" placeholder="0.00">
                        </div>
                        <div class="debt-effect"></div>
                    </div>
                    <div>
                        <label class="form-label fw-bold mb-1" for="debt-note">Note</label>
                        <input type="text" id="debt-note" class="form-control form-control-sm" placeholder="Optional note">
                    </div>
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: 'Save',
            confirmButtonColor: '#0aa2a2',
            cancelButtonText: 'Cancel',
            didOpen: (popup) => {
                const $popup = $(popup);
                const refresh = () => {
                    const owesMore = $popup.find('input[name="debt-direction"]:checked').val() === 'true';
                    const amount = parseFloat($popup.find('#debt-amount').val());
                    const amountText = !isNaN(amount) && amount > 0 ? '$' + amount.toFixed(2) : 'the amount';

                    $popup.find('.debt-amount-sign')
                        .text(owesMore ? '+' : '−')
                        .toggleClass('is-plus', owesMore)
                        .toggleClass('is-minus', !owesMore);
                    $popup.find('.debt-effect')
                        .toggleClass('is-plus', owesMore)
                        .toggleClass('is-minus', !owesMore)
                        .html(owesMore
                            ? `<i class="fas fa-arrow-up me-1"></i>Customer's debt increases by ${amountText}`
                            : `<i class="fas fa-arrow-down me-1"></i>Customer's debt decreases by ${amountText}`);
                };
                $popup.on('change input', 'input[name="debt-direction"], #debt-amount', refresh);
                refresh();
                $popup.find('#debt-amount').trigger('focus');
            },
            preConfirm: () => {
                const amount = parseFloat($('#debt-amount').val());
                if (isNaN(amount) || amount <= 0) {
                    Swal.showValidationMessage('Please enter a valid amount greater than zero.');
                    return false;
                }
                return {
                    customerOwesMore: $('input[name="debt-direction"]:checked').val() === 'true',
                    amount: amount,
                    note: $('#debt-note').val()
                };
            }
        }).then((result) => {
            if (!result.isConfirmed) return;

            $.ajax({
                url: url,
                type: 'POST',
                data: {
                    CustomerId: customerId,
                    Amount: result.value.amount,
                    CustomerOwesMore: result.value.customerOwesMore,
                    Note: result.value.note,
                    __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
                },
                success: function (response) {
                    if (response.success) {
                        Swal.fire({
                            title: 'Saved!',
                            text: response.message,
                            icon: 'success',
                            timer: 1500,
                            showConfirmButton: false
                        }).then(() => { if (onSaved) onSaved(); });
                    } else {
                        Swal.fire('Error!', response.message, 'error');
                    }
                },
                error: function () {
                    Swal.fire('Error!', 'An error occurred while saving the transaction.', 'error');
                }
            });
        });
    };
})();
