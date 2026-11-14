package com.nexpos.nexpos_owner

import android.view.WindowManager
import io.flutter.embedding.android.FlutterFragmentActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

/**
 * A fragment activity because the fingerprint / face prompt (local_auth) needs one.
 * It also offers one switch to the app: hide the screen from screenshots and from the recent-apps thumbnail, used while the
 * app lock is on so the shop's figures are not visible in the app switcher.
 */
class MainActivity : FlutterFragmentActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "nexpos/privacy").setMethodCallHandler { call, result ->
            if (call.method == "hideFromRecents") {
                val hide = call.arguments as? Boolean ?: false
                if (hide) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
                else window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
                result.success(null)
            } else {
                result.notImplemented()
            }
        }
    }
}
