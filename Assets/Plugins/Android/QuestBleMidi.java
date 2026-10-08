package com.questpianomotion.midi;

import android.Manifest;
import android.bluetooth.*;
import android.bluetooth.le.*;
import android.content.*;
import android.content.pm.PackageManager;

import android.os.*;
import android.util.Base64;
import com.unity3d.player.UnityPlayer;

import java.nio.charset.StandardCharsets;
import java.util.*;

/** Standard BLE-MIDI GATT central. Notification payloads are decoded by the Unity consumer. */
public final class QuestBleMidi {
    private final Context context = UnityPlayer.currentActivity.getApplicationContext();
    private final Handler main = new Handler(Looper.getMainLooper());
    private static final UUID SERVICE = UUID.fromString("03B80E5A-EDE8-4B33-A751-6CE34EC4C700");
    private static final UUID CHARACTERISTIC = UUID.fromString("7772E5DB-3868-4112-A1A9-F2669D106BF3");
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805F9B34FB");
    private final BluetoothManager bluetooth = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
    private final Map<String, BluetoothDevice> found = new LinkedHashMap<>();
    private final ArrayDeque<String> events = new ArrayDeque<>();
    private BluetoothLeScanner scanner;
    private ScanCallback scanCallback;
    private BluetoothGatt gatt;
    private BluetoothGattCharacteristic characteristic;
    private long generation, request, scanRequest, dropped, stale;
    private String state = "idle", scanState = "idle", address = "", name = "";
    private boolean closed;
    private final BroadcastReceiver bluetoothState = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent intent) {
            synchronized (QuestBleMidi.this) {
                if (BluetoothAdapter.ACTION_STATE_CHANGED.equals(intent.getAction()) &&
                    intent.getIntExtra(BluetoothAdapter.EXTRA_STATE, -1) != BluetoothAdapter.STATE_ON) {
                    stopScanInternal(); disconnectInternal("bluetooth_disabled");
                }

            }
        }
    };
    @SuppressWarnings("deprecation")
    public QuestBleMidi() {

        IntentFilter filter = new IntentFilter(BluetoothAdapter.ACTION_STATE_CHANGED);

        if (Build.VERSION.SDK_INT >= 33) context.registerReceiver(bluetoothState, filter, Context.RECEIVER_EXPORTED);
        else context.registerReceiver(bluetoothState, filter);
    }
    public String diagnostics() {
        boolean feature = context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_MIDI);
        String service;
        try {
            android.content.pm.ServiceInfo info = context.getPackageManager().getServiceInfo(
                new ComponentName("com.android.bluetoothmidiservice", "com.android.bluetoothmidiservice.BluetoothMidiService"), 0);
            service = info.enabled && info.applicationInfo.enabled ? "present_enabled (binding not tested)" : "disabled";
        } catch (PackageManager.NameNotFoundException e) { service = "unavailable_not_found"; }
        return "android.software.midi=" + feature + "; BluetoothMidiService=" + service + "; transport=Direct GATT BLE-MIDI";
    }
    public long nowNanos() { return System.nanoTime(); }
    public synchronized String capability() {
        if (closed) return "closed";
        if (!context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_BLUETOOTH_LE)) return "ble_unsupported";

        if (context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED ||
            context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) return "permission_denied";
        if (bluetooth == null || bluetooth.getAdapter() == null) return "ble_unsupported";
        try { if (!bluetooth.getAdapter().isEnabled()) return "bluetooth_disabled"; }
        catch (SecurityException e) { return "permission_denied"; }
        return "ready";
    }
    public synchronized String status() { return generation + "|" + state + "|" + encode(name) + "|" + address + "|" + "direct_gatt" + "|" + scanState; }
    public synchronized long dropped() { return dropped; }
    public synchronized long stale() { return stale; }
    public synchronized String listDevices() {
        StringBuilder b = new StringBuilder();
        for (Map.Entry<String, BluetoothDevice> entry : found.entrySet()) {
            if (b.length() > 0) b.append('\n');
            b.append(entry.getKey()).append('|').append(encode(safeName(entry.getValue())));
        }
        return b.toString();
    }
    public synchronized String poll(int limit) {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < Math.min(limit, 256) && !events.isEmpty(); ++i) {
            if (b.length() > 0) b.append('\n');
            b.append(events.removeFirst());
        }
        return b.toString();
    }
    public void scan() { main.post(() -> { synchronized (this) {
        stopScanInternal();
        String available = capability();
        if (!available.equals("ready")) { scanState = available; return; }
        found.clear();
        final long scanId = ++scanRequest;
        try { scanner = bluetooth.getAdapter().getBluetoothLeScanner(); }
        catch (SecurityException e) { scanState = "permission_denied"; return; }
        if (scanner == null) { scanState = "scanner_unavailable"; return; }
        scanCallback = new ScanCallback() {
            @Override public void onScanResult(int type, ScanResult result) { synchronized (QuestBleMidi.this) {
                if (closed || scanId != scanRequest) return;
                // Only devices advertising the standard BLE-MIDI service are selectable.
                String target = safeAddress(result.getDevice());
                if (target.isEmpty()) { stopScanInternal(); scanState = "permission_denied"; return; }
                if (found.size() < 128) found.put(target, result.getDevice());
            } }
            @Override public void onScanFailed(int error) { synchronized (QuestBleMidi.this) {
                if (scanId == scanRequest) { stopScanInternal(); scanState = "scan_failed_" + error; }
            } }
        };
        try {
            scanner.startScan(Collections.singletonList(new ScanFilter.Builder().setServiceUuid(
                ParcelUuid.fromString("03B80E5A-EDE8-4B33-A751-6CE34EC4C700")).build()),
                new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build(), scanCallback);
            scanState = "scanning";
            main.postDelayed(() -> { synchronized (this) {
                if (scanId == scanRequest) { stopScanInternal(); scanState = "scan_timeout"; }
            } }, 12000);
        } catch (RuntimeException e) { stopScanInternal(); scanState = "scan_error_" + e.getClass().getSimpleName(); }
    } }); }
    public void stopScan() { main.post(() -> { synchronized (this) { stopScanInternal(); } }); }
    private void stopScanInternal() {
        ++scanRequest;
        if (scanner != null && scanCallback != null) try { scanner.stopScan(scanCallback); } catch (RuntimeException ignored) { }
        scanner = null; scanCallback = null; scanState = "stopped";
    }
    @SuppressWarnings("deprecation")
    public void connect(String target, long epoch) { main.post(() -> { synchronized (this) {
        if (closed) return;
        disconnectInternal("idle"); stopScanInternal(); generation = epoch;
        String available = capability();
        if (!available.equals("ready")) { state = available; return; }
        final BluetoothDevice selected = found.get(target);
        if (selected == null) { state = "target_not_scanned"; return; }
        address = target; name = safeName(selected);
        final long attempt = ++request;
        state = "connecting";
        BluetoothGattCallback callback = new BluetoothGattCallback() {
            private boolean current(BluetoothGatt remote) {
                if (!closed && attempt == request && remote == gatt) return true;
                ++stale;
                if (remote != gatt) try { remote.close(); } catch (RuntimeException ignored) { }
                return false;
            }
            @Override public void onConnectionStateChange(BluetoothGatt remote, int status, int next) {
                main.post(() -> { synchronized (QuestBleMidi.this) {
                    if (!current(remote)) return;
                    if (status != BluetoothGatt.GATT_SUCCESS || next == BluetoothProfile.STATE_DISCONNECTED) {
                        disconnectInternal("gatt_disconnected_" + status); return;
                    }
                    if (next == BluetoothProfile.STATE_CONNECTED) {
                        state = "discovering";
                        try { if (!remote.discoverServices()) disconnectInternal("discovery_start_failed"); }
                        catch (RuntimeException e) { disconnectInternal("discovery_error_" + e.getClass().getSimpleName()); }
                    }
                } });
            }
            @Override public void onServicesDiscovered(BluetoothGatt remote, int status) {
                main.post(() -> { synchronized (QuestBleMidi.this) {
                    if (!current(remote)) return;
                    if (status != BluetoothGatt.GATT_SUCCESS) { disconnectInternal("discovery_failed_" + status); return; }
                    BluetoothGattService service = remote.getService(SERVICE);
                    characteristic = service == null ? null : service.getCharacteristic(CHARACTERISTIC);
                    if (characteristic == null) { disconnectInternal("ble_midi_characteristic_missing"); return; }
                    if ((characteristic.getProperties() & BluetoothGattCharacteristic.PROPERTY_NOTIFY) == 0) {
                        disconnectInternal("ble_midi_notify_missing"); return;
                    }
                    BluetoothGattDescriptor descriptor = characteristic.getDescriptor(CCCD);
                    if (descriptor == null) { disconnectInternal("cccd_missing"); return; }
                    try {
                        state = "subscribing";
                        if (!remote.setCharacteristicNotification(characteristic, true)) { disconnectInternal("notify_enable_failed"); return; }
                        boolean started;
                        if (Build.VERSION.SDK_INT >= 33) started = remote.writeDescriptor(descriptor,
                            BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE) == BluetoothStatusCodes.SUCCESS;
                        else { descriptor.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE); started = remote.writeDescriptor(descriptor); }
                        if (!started) disconnectInternal("cccd_write_start_failed");
                    } catch (RuntimeException e) { disconnectInternal("subscribe_error_" + e.getClass().getSimpleName()); }
                } });
            }
            @Override public void onDescriptorWrite(BluetoothGatt remote, BluetoothGattDescriptor descriptor, int status) {
                main.post(() -> { synchronized (QuestBleMidi.this) {
                    if (!current(remote) || !CCCD.equals(descriptor.getUuid()) || descriptor.getCharacteristic() != characteristic) return;
                    if (status == BluetoothGatt.GATT_SUCCESS) state = "connected";
                    else disconnectInternal("cccd_write_failed_" + status);
                } });
            }
            private void receive(BluetoothGatt remote, BluetoothGattCharacteristic source, byte[] bytes) {
                long received = System.nanoTime();
                synchronized (QuestBleMidi.this) {
                    if (!current(remote)) return;
                    // Notifications can arrive just before the successful CCCD callback.
                    if (source != characteristic || !(state.equals("connected") || state.equals("subscribing"))) return;
                    if (bytes == null || bytes.length == 0 || bytes.length > 512) { ++dropped; return; }
                    if (events.size() >= 2048) ++dropped;
                    else events.addLast(epoch + "|" + received + "|" + Base64.encodeToString(bytes, Base64.NO_WRAP));
                }
            }
            @Override public void onCharacteristicChanged(BluetoothGatt remote, BluetoothGattCharacteristic source, byte[] bytes) {
                receive(remote, source, bytes);
            }
            @SuppressWarnings("deprecation")
            @Override public void onCharacteristicChanged(BluetoothGatt remote, BluetoothGattCharacteristic source) {
                byte[] bytes = source.getValue();
                receive(remote, source, bytes == null ? null : bytes.clone());
            }
        };
        try {
            gatt = selected.connectGatt(context, false, callback, BluetoothDevice.TRANSPORT_LE);
            if (gatt == null) disconnectInternal("gatt_connect_failed");
        } catch (RuntimeException e) { disconnectInternal("connect_error_" + e.getClass().getSimpleName()); }
        main.postDelayed(() -> { synchronized (this) {
            if (attempt == request && !state.equals("connected")) disconnectInternal("connect_timeout");
        } }, 15000);
    } }); }
    public void disconnect() { main.post(() -> { synchronized (this) { disconnectInternal("disconnected"); } }); }
    private void disconnectInternal(String nextState) {
        ++request; state = nextState; stale += events.size(); events.clear();
        BluetoothGatt old = gatt; gatt = null; characteristic = null;
        if (old != null) {
            try { old.disconnect(); } catch (RuntimeException ignored) { }
            try { old.close(); } catch (RuntimeException ignored) { }
        }
    }
    public void close() { main.post(() -> { synchronized (this) {
        if (closed) return;
        closed = true; stopScanInternal(); disconnectInternal("closed");

        context.unregisterReceiver(bluetoothState);
        main.removeCallbacksAndMessages(null);
    } }); }
    private static String safeAddress(BluetoothDevice d) { try { return d.getAddress(); } catch (SecurityException e) { return ""; } }
    private static String safeName(BluetoothDevice d) { try { return d.getName(); } catch (SecurityException e) { return ""; } }
    private static String encode(String text) { return Base64.encodeToString((text == null ? "Unnamed BLE MIDI" : text).getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP); }
}
