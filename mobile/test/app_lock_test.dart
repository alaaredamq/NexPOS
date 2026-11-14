import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/state/app_lock.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fakes.dart';

void main() {
  late FakeBiometrics bio;
  late FakePrivacy privacy;
  late DateTime now;

  Future<AppLock> lock({bool enabled = false}) async {
    SharedPreferences.setMockInitialValues({'app_lock': enabled});
    bio = FakeBiometrics();
    privacy = FakePrivacy();
    now = DateTime(2026, 11, 12, 9);
    return AppLock(prefs: await SharedPreferences.getInstance(), biometrics: bio, privacy: privacy, clock: () => now);
  }

  test('off by default: nothing is locked and nothing is hidden', () async {
    final l = await lock();

    expect(l.enabled, isFalse);
    expect(l.locked, isFalse);
    expect(privacy.hidden, isFalse);
  });

  test('turning it on needs the owner to prove who they are, and a phone with no screen lock cannot turn it on', () async {
    final l = await lock();

    bio.supported = false;
    expect(await l.setEnabled(true, reason: 'x'), SetLock.unsupported);
    expect(l.enabled, isFalse);
    expect(bio.asked, 0);

    bio.supported = true;
    bio.accept = false;
    expect(await l.setEnabled(true, reason: 'Confirm it is you'), SetLock.cancelled);
    expect(l.enabled, isFalse);
    expect(bio.lastReason, 'Confirm it is you');

    bio.accept = true;
    expect(await l.setEnabled(true, reason: 'x'), SetLock.done);
    expect(l.enabled, isTrue);
    expect(l.locked, isFalse, reason: 'the owner just proved who they are');
    expect(privacy.hidden, isTrue, reason: 'hidden from the recent-apps view while the lock is on');
  });

  test('turning it off also asks, so whoever holds an unlocked phone cannot just remove the lock', () async {
    final l = await lock(enabled: true);
    await l.unlock('x');

    bio.accept = false;
    expect(await l.setEnabled(false, reason: 'x'), SetLock.cancelled);
    expect(l.enabled, isTrue);

    bio.accept = true;
    expect(await l.setEnabled(false, reason: 'x'), SetLock.done);
    expect(l.enabled, isFalse);
    expect(privacy.hidden, isFalse);
  });

  test('a cold start with the lock on is locked, and stays locked until the owner passes', () async {
    final l = await lock(enabled: true);

    expect(l.locked, isTrue);
    expect(privacy.hidden, isTrue);

    bio.accept = false;
    expect(await l.unlock('x'), isFalse);
    expect(l.locked, isTrue);

    bio.accept = true;
    expect(await l.unlock('x'), isTrue);
    expect(l.locked, isFalse);
  });

  test('a short trip to another app does not ask again, a longer one does', () async {
    final l = await lock(enabled: true);
    await l.unlock('x');

    l.didChangeAppLifecycleState(AppLifecycleState.paused);
    now = now.add(const Duration(seconds: 10));
    l.didChangeAppLifecycleState(AppLifecycleState.resumed);
    expect(l.locked, isFalse, reason: 'ten seconds away is within the grace period');

    l.didChangeAppLifecycleState(AppLifecycleState.paused);
    now = now.add(const Duration(minutes: 2));
    l.didChangeAppLifecycleState(AppLifecycleState.resumed);
    expect(l.locked, isTrue);
  });

  test('the system prompt itself (the app goes inactive, then resumes) never re-locks the app', () async {
    final l = await lock(enabled: true);

    l.didChangeAppLifecycleState(AppLifecycleState.inactive);
    now = now.add(const Duration(minutes: 5));
    l.didChangeAppLifecycleState(AppLifecycleState.resumed);

    await l.unlock('x');
    expect(l.locked, isFalse);
  });

  test('with the lock off, being away never locks anything', () async {
    final l = await lock();

    l.didChangeAppLifecycleState(AppLifecycleState.paused);
    now = now.add(const Duration(hours: 3));
    l.didChangeAppLifecycleState(AppLifecycleState.resumed);

    expect(l.locked, isFalse);
  });

  test('the choice survives closing the app', () async {
    final first = await lock();
    await first.setEnabled(true, reason: 'x');

    final prefs = await SharedPreferences.getInstance();
    final second = AppLock(prefs: prefs, biometrics: bio, privacy: privacy);

    expect(second.enabled, isTrue);
    expect(second.locked, isTrue);
  });
}
