import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/admin_api.dart';
import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';

/// A day picker field: shows the day, opens the calendar on tap.
class DayField extends StatelessWidget {
  const DayField({super.key, required this.label, required this.value, required this.onChanged, this.last});
  final String label;
  final DateTime value;
  final ValueChanged<DateTime> onChanged;
  final DateTime? last;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(12),
      onTap: () async {
        final picked = await showDatePicker(
          context: context,
          initialDate: value,
          firstDate: DateTime(2020),
          lastDate: last ?? DateTime.now().add(const Duration(days: 1)),
        );
        if (picked != null) onChanged(picked);
      },
      child: InputDecorator(
        decoration: InputDecoration(labelText: label, suffixIcon: const Icon(Icons.calendar_today, size: 18)),
        child: Text(formatDay(value)),
      ),
    );
  }
}

class _ClientChoice {
  _ClientChoice(this.id, this.name);
  final int? id;
  final String name;
}

/// Records or corrects a payment. With [paymentId] it edits that one (and can delete it); with [clientId] the client is fixed.
class PaymentFormScreen extends StatefulWidget {
  const PaymentFormScreen({super.key, this.paymentId, this.clientId, this.clientName, this.purposes = const []});
  final int? paymentId;
  final int? clientId;
  final String? clientName;

  /// Ticked to start with on a new payment.
  final List<String> purposes;

  @override
  State<PaymentFormScreen> createState() => _PaymentFormScreenState();
}

class _PaymentFormScreenState extends State<PaymentFormScreen> {
  final _amount = TextEditingController();
  final _payer = TextEditingController();
  final _reference = TextEditingController();
  final _note = TextEditingController();
  FinanceOptions? _options;
  List<_ClientChoice> _clients = const [];
  int? _clientId;
  DateTime _date = DateTime.now();
  String _method = 'Cash';
  Set<String> _purposes = {};
  bool _busy = false;
  String? _error;
  Object? _loadError;

  AdminApi get _api => context.read<Session>().api;
  bool get _editing => widget.paymentId != null;

  @override
  void initState() {
    super.initState();
    _clientId = widget.clientId;
    _purposes = {...widget.purposes};
    _load();
  }

  @override
  void dispose() {
    for (final c in [_amount, _payer, _reference, _note]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final options = await _api.financeOptions();
      final clients = widget.clientId == null && !_editing ? await _api.clients() : const <ClientRow>[];
      Payment? p;
      if (_editing) p = await _api.payment(widget.paymentId!);
      if (!mounted) return;
      setState(() {
        _options = options;
        _clients = [_ClientChoice(null, 'Other income (not a client)'), for (final c in clients) _ClientChoice(c.id, c.name)];
        _date = p?.date ?? options.today;
        _method = p?.method ?? (options.methods.isEmpty ? 'Cash' : options.methods.first);
        if (p != null) {
          _clientId = p.clientId;
          _amount.text = p.amount == p.amount.roundToDouble() ? p.amount.toStringAsFixed(0) : p.amount.toStringAsFixed(2);
          _payer.text = p.clientId == null ? p.payer : '';
          _reference.text = p.reference ?? '';
          _note.text = p.note ?? '';
          _purposes = {...p.purposes};
        }
        if (_purposes.isEmpty && options.purposes.isNotEmpty) _purposes = {options.purposes.first};
      });
    } catch (e) {
      if (mounted) setState(() => _loadError = e);
    }
  }

  Future<void> _save() async {
    final amount = parseAmount(_amount.text);
    final error = amount == null || amount <= 0
        ? 'Enter the amount received, more than 0.'
        : _purposes.isEmpty
            ? 'Tick what the payment was for.'
            : (_clientId == null && widget.clientId == null && _payer.text.trim().isEmpty && !_editing)
                ? 'Pick the client who paid, or say where the money came from.'
                : null;
    if (error != null) {
      setState(() => _error = error);
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    // Keep the form's order, like the portal, so the same choice always reads the same.
    final ordered = [for (final p in _options!.purposes) if (_purposes.contains(p)) p, ..._purposes.where((p) => !_options!.purposes.contains(p))];
    try {
      await _api.savePayment(
        id: widget.paymentId,
        clientId: _clientId,
        payer: _clientId == null ? _payer.text.trim() : null,
        amount: amount!,
        date: _date,
        method: _method,
        purposes: ordered,
        reference: _reference.text.trim(),
        note: _note.text.trim(),
      );
      if (!mounted) return;
      toast(context, _editing ? 'Payment saved' : 'Payment of ${context.money(amount)} recorded');
      Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } on OfflineException catch (e) {
      setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete() async {
    if (!await confirm(context, title: 'Delete this payment?', message: 'It comes off every total.', yes: 'Delete', danger: true)) return;
    if (mounted && await act(context, () => _api.deletePayment(widget.paymentId!), done: 'Payment deleted') && mounted) Navigator.pop(context, true);
  }

  @override
  Widget build(BuildContext context) {
    final o = _options;
    return Scaffold(
      appBar: AppBar(
        title: Text(_editing ? 'Payment' : 'Record a payment'),
        actions: [if (_editing) IconButton(tooltip: 'Delete', onPressed: _delete, icon: const Icon(Icons.delete_outline, color: Tokens.bad))],
      ),
      body: o == null
          ? Center(child: _loadError == null ? const CircularProgressIndicator() : Text('$_loadError'))
          : ListView(padding: const EdgeInsets.all(16), children: [
              if (widget.clientName != null)
                Padding(padding: const EdgeInsets.only(bottom: 12), child: Text('From ${widget.clientName}', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)))
              else if (!_editing) ...[
                DropdownButtonFormField<int?>(
                  initialValue: _clientId,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'From'),
                  items: [for (final c in _clients) DropdownMenuItem(value: c.id, child: Text(c.name, overflow: TextOverflow.ellipsis))],
                  onChanged: (v) => setState(() => _clientId = v),
                ),
                const SizedBox(height: 12),
              ],
              if (_clientId == null && widget.clientId == null) ...[
                TextField(controller: _payer, decoration: const InputDecoration(labelText: 'Where it came from', hintText: 'Capital, loan, refund...')),
                const SizedBox(height: 12),
              ],
              Row(children: [
                Expanded(
                  child: TextField(
                    controller: _amount,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: InputDecoration(labelText: 'Amount (${o.currency})'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(child: DayField(label: 'Received on', value: _date, onChanged: (d) => setState(() => _date = d))),
              ]),
              const SizedBox(height: 16),
              const Text('For (tick all it covers)', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
              const SizedBox(height: 8),
              Wrap(spacing: 8, runSpacing: 8, children: [
                for (final p in {...o.purposes, ..._purposes})
                  FilterChip(
                    label: Text(p),
                    selected: _purposes.contains(p),
                    onSelected: (on) => setState(() => on ? _purposes.add(p) : _purposes.remove(p)),
                  ),
              ]),
              const SizedBox(height: 16),
              const Text('Paid by', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
              const SizedBox(height: 8),
              Wrap(spacing: 8, runSpacing: 8, children: [
                for (final m in {...o.methods, _method})
                  ChoiceChip(label: Text(m), selected: _method == m, onSelected: (_) => setState(() => _method = m)),
              ]),
              const SizedBox(height: 16),
              TextField(controller: _reference, decoration: const InputDecoration(labelText: 'Receipt or transfer no. (optional)')),
              const SizedBox(height: 12),
              TextField(
                controller: _note,
                maxLines: 3,
                minLines: 1,
                decoration: const InputDecoration(labelText: 'Note (optional)', hintText: 'For several things, the split: licence 300, Online 120...'),
              ),
              if (_error != null) ...[const SizedBox(height: 12), Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600))],
              const SizedBox(height: 20),
              FilledButton(onPressed: _busy ? null : _save, child: Text(_editing ? 'Save payment' : 'Record payment')),
            ]),
    );
  }
}

/// Records or corrects an expense (money out).
class ExpenseFormScreen extends StatefulWidget {
  const ExpenseFormScreen({super.key, this.expenseId});
  final int? expenseId;

  @override
  State<ExpenseFormScreen> createState() => _ExpenseFormScreenState();
}

class _ExpenseFormScreenState extends State<ExpenseFormScreen> {
  final _amount = TextEditingController();
  final _category = TextEditingController();
  final _payee = TextEditingController();
  final _note = TextEditingController();
  FinanceOptions? _options;
  DateTime _date = DateTime.now();
  bool _busy = false;
  String? _error;
  Object? _loadError;

  AdminApi get _api => context.read<Session>().api;
  bool get _editing => widget.expenseId != null;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    for (final c in [_amount, _category, _payee, _note]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final options = await _api.financeOptions();
      final e = _editing ? await _api.expense(widget.expenseId!) : null;
      if (!mounted) return;
      setState(() {
        _options = options;
        _date = e?.date ?? options.today;
        if (e != null) {
          _amount.text = e.amount == e.amount.roundToDouble() ? e.amount.toStringAsFixed(0) : e.amount.toStringAsFixed(2);
          _category.text = e.category;
          _payee.text = e.payee ?? '';
          _note.text = e.note ?? '';
        }
      });
    } catch (e) {
      if (mounted) setState(() => _loadError = e);
    }
  }

  Future<void> _save() async {
    final amount = parseAmount(_amount.text);
    if (amount == null || amount <= 0 || _category.text.trim().isEmpty) {
      setState(() => _error = amount == null || amount <= 0 ? 'Enter the amount spent, more than 0.' : 'Pick what the money was spent on.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await _api.saveExpense(id: widget.expenseId, amount: amount, date: _date, category: _category.text.trim(), payee: _payee.text.trim(), note: _note.text.trim());
      if (!mounted) return;
      toast(context, _editing ? 'Expense saved' : 'Expense of ${context.money(amount)} recorded');
      Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } on OfflineException catch (e) {
      setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete() async {
    if (!await confirm(context, title: 'Delete this expense?', message: 'It comes off every total.', yes: 'Delete', danger: true)) return;
    if (mounted && await act(context, () => _api.deleteExpense(widget.expenseId!), done: 'Expense deleted') && mounted) Navigator.pop(context, true);
  }

  @override
  Widget build(BuildContext context) {
    final o = _options;
    return Scaffold(
      appBar: AppBar(
        title: Text(_editing ? 'Expense' : 'Record an expense'),
        actions: [if (_editing) IconButton(tooltip: 'Delete', onPressed: _delete, icon: const Icon(Icons.delete_outline, color: Tokens.bad))],
      ),
      body: o == null
          ? Center(child: _loadError == null ? const CircularProgressIndicator() : Text('$_loadError'))
          : ListView(padding: const EdgeInsets.all(16), children: [
              Row(children: [
                Expanded(
                  child: TextField(
                    controller: _amount,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: InputDecoration(labelText: 'Amount (${o.currency})'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(child: DayField(label: 'Paid on', value: _date, onChanged: (d) => setState(() => _date = d))),
              ]),
              const SizedBox(height: 16),
              const Text('Spent on', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
              const SizedBox(height: 8),
              Wrap(spacing: 8, runSpacing: 8, children: [
                for (final c in o.expenseCategories)
                  ChoiceChip(label: Text(c), selected: _category.text == c, onSelected: (_) => setState(() => _category.text = c)),
              ]),
              const SizedBox(height: 12),
              TextField(controller: _category, decoration: const InputDecoration(labelText: 'Or type another'), onChanged: (_) => setState(() {})),
              const SizedBox(height: 12),
              TextField(controller: _payee, decoration: const InputDecoration(labelText: 'Paid to (optional)')),
              const SizedBox(height: 12),
              TextField(controller: _note, maxLines: 3, minLines: 1, decoration: const InputDecoration(labelText: 'Note (optional)')),
              if (_error != null) ...[const SizedBox(height: 12), Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600))],
              const SizedBox(height: 20),
              FilledButton(onPressed: _busy ? null : _save, child: Text(_editing ? 'Save expense' : 'Record expense')),
            ]),
    );
  }
}

/// The monthly budget: an income target and a spending limit, in force from a month until a later one replaces it.
class BudgetScreen extends StatefulWidget {
  const BudgetScreen({super.key, required this.month});

  /// yyyy-MM: the month the Finance tab shows.
  final String month;

  @override
  State<BudgetScreen> createState() => _BudgetScreenState();
}

class _BudgetScreenState extends State<BudgetScreen> {
  final _income = TextEditingController();
  final _spending = TextEditingController();
  final _list = GlobalKey<LoaderState<List<Budget>>>();
  bool _filled = false;

  @override
  void dispose() {
    _income.dispose();
    _spending.dispose();
    super.dispose();
  }

  String _plain(double? v) => v == null ? '' : (v == v.roundToDouble() ? v.toStringAsFixed(0) : v.toStringAsFixed(2));

  Future<void> _save() async {
    final api = context.read<Session>().api;
    final income = parseAmount(_income.text);
    final spending = parseAmount(_spending.text);
    if (await act(context, () => api.setBudget(widget.month, incomeTarget: income, spendingLimit: spending), done: 'Budget saved from ${monthLabel(widget.month)}')) {
      await _list.currentState?.reload();
    }
  }

  @override
  Widget build(BuildContext context) {
    final currency = context.read<Session>().currency;
    return Scaffold(
      appBar: AppBar(title: const Text('Budget')),
      body: Loader<List<Budget>>(
        key: _list,
        load: (api) => api.budgets(),
        builder: (context, budgets, reload) {
          // Fill the form once with the budget in force this month: the latest set at or before it.
          if (!_filled) {
            final inForce = budgets.where((b) => b.month.compareTo(widget.month) <= 0).fold<Budget?>(null, (a, b) => a == null || b.month.compareTo(a.month) > 0 ? b : a);
            _income.text = _plain(inForce?.incomeTarget);
            _spending.text = _plain(inForce?.spendingLimit);
            _filled = true;
          }
          return PageList(children: [
            SectionCard(title: 'From ${monthLabel(widget.month)} on', children: [
              const MutedText('Applies from this month until a later budget replaces it. Leave a box empty for none.', size: 13),
              const SizedBox(height: 12),
              TextField(
                controller: _income,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(labelText: 'Income target ($currency a month)'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _spending,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(labelText: 'Spending limit ($currency a month)'),
              ),
              const SizedBox(height: 16),
              FilledButton(onPressed: _save, child: const Text('Save budget')),
            ]),
            SectionCard(title: 'Budgets set', children: [
              if (budgets.isEmpty) const MutedText('None yet.'),
              for (final b in budgets.reversed)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: Text('From ${monthLabel(b.month)}', style: const TextStyle(fontWeight: FontWeight.w600)),
                  subtitle: Text([
                    'income ${b.incomeTarget == null ? 'none' : formatMoney(b.incomeTarget!, currency)}',
                    'spending ${b.spendingLimit == null ? 'none' : formatMoney(b.spendingLimit!, currency)}',
                  ].join(' · ')),
                  trailing: IconButton(
                    tooltip: 'Delete',
                    icon: const Icon(Icons.delete_outline),
                    onPressed: () async {
                      final api = context.read<Session>().api;
                      if (!await confirm(context, title: 'Delete this budget?', message: 'The earlier budget applies again.', yes: 'Delete', danger: true)) return;
                      if (context.mounted && await act(context, () => api.deleteBudget(b.month), done: 'Budget deleted')) await reload();
                    },
                  ),
                ),
            ]),
          ]);
        },
      ),
    );
  }
}
