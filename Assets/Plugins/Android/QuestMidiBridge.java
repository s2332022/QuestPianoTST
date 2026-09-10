package com.questpianomotion.midi;

import android.content.Context;
import android.media.midi.MidiDevice;
import android.media.midi.MidiDeviceInfo;
import android.media.midi.MidiDeviceStatus;
import android.media.midi.MidiManager;
import android.media.midi.MidiOutputPort;
import android.media.midi.MidiReceiver;
import android.os.Handler;
import android.os.Looper;
import android.os.Build;
import android.util.Base64;

import com.unity3d.player.UnityPlayer;

import java.io.IOException;
import java.nio.charset.StandardCharsets;

/** Android API 23+ USB MIDI bridge. No third-party library is used. */
public final class QuestMidiBridge {
    private final String gameObjectName;
    private final MidiManager midiManager;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private MidiDevice openDevice;
    private MidiOutputPort outputPort;
    private String openDeviceName = "";

    private int runningStatus = 0;
    private int data1 = -1;

    private final MidiReceiver receiver = new MidiReceiver() {
        @Override
        public void onSend(byte[] data, int offset, int count, long timestamp) {
            final long eventTime = timestamp > 0 ? timestamp : System.nanoTime();
            parseBytes(data, offset, count, eventTime);
        }
    };

    private final MidiManager.DeviceCallback deviceCallback = new MidiManager.DeviceCallback() {
        @Override public void onDeviceAdded(MidiDeviceInfo info) { devicesChanged(); }
        @Override public void onDeviceRemoved(MidiDeviceInfo info) {
            if (openDevice != null && openDevice.getInfo().getId() == info.getId()) closeCurrent();
            devicesChanged();
        }
        @Override public void onDeviceStatusChanged(MidiDeviceStatus status) { devicesChanged(); }
    };

    public QuestMidiBridge(String gameObjectName) {
        this.gameObjectName = gameObjectName;
        Context context = UnityPlayer.currentActivity.getApplicationContext();
        midiManager = (MidiManager) context.getSystemService(Context.MIDI_SERVICE);
        if (midiManager != null) registerDeviceCallback(context);
    }

    public long nowNanos() { return System.nanoTime(); }

    public String listDevices() {
        if (midiManager == null) return "";
        StringBuilder result = new StringBuilder();
        MidiDeviceInfo[] devices = midi1Devices();
        for (MidiDeviceInfo device : devices) {
            if (!hasOutputPort(device)) continue;
            if (result.length() > 0) result.append('\n');
            result.append(device.getId()).append('|').append(encodeName(deviceName(device)));
        }
        return result.toString();
    }

    public void openDevice(int deviceId) {
        if (midiManager == null) return;
        closeCurrent();
        MidiDeviceInfo target = null;
        for (MidiDeviceInfo info : midi1Devices()) {
            if (info.getId() == deviceId) { target = info; break; }
        }
        if (target == null) { connection(false, ""); return; }
        final MidiDeviceInfo selected = target;
        midiManager.openDevice(selected, device -> {
            if (device == null) { connection(false, deviceName(selected)); return; }
            openDevice = device;
            openDeviceName = deviceName(selected);
            for (MidiDeviceInfo.PortInfo portInfo : selected.getPorts()) {
                if (portInfo.getType() != MidiDeviceInfo.PortInfo.TYPE_OUTPUT) continue;
                outputPort = device.openOutputPort(portInfo.getPortNumber());
                if (outputPort != null) {
                    outputPort.connect(receiver);
                    connection(true, openDeviceName);
                    return;
                }
            }
            closeCurrent();
        }, mainHandler);
    }

    public void close() {
        if (midiManager != null) midiManager.unregisterDeviceCallback(deviceCallback);
        closeCurrent();
    }

    private synchronized void parseBytes(byte[] bytes, int offset, int count, long timestamp) {
        for (int i = offset; i < offset + count; ++i) {
            int value = bytes[i] & 0xFF;
            if (value >= 0xF8) continue; // real-time bytes do not cancel running status
            if ((value & 0x80) != 0) {
                if (value >= 0xF0) { runningStatus = 0; data1 = -1; continue; }
                runningStatus = value;
                data1 = -1;
                continue;
            }
            int command = runningStatus & 0xF0;
            if (runningStatus == 0 || (command != 0x80 && command != 0x90 && command != 0xB0)) continue;
            if (data1 < 0) data1 = value;
            else {
                sendMidi(timestamp, runningStatus, data1, value);
                data1 = -1;
            }
        }
    }

    private void sendMidi(long timestamp, int status, int first, int second) {
        UnityPlayer.UnitySendMessage(gameObjectName, "OnAndroidMidiMessage",
                timestamp + "|" + status + "|" + first + "|" + second);
    }

    private void connection(boolean connected, String name) {
        UnityPlayer.UnitySendMessage(gameObjectName, "OnAndroidMidiConnection",
                (connected ? "1|" : "0|") + encodeName(name));
    }

    private void devicesChanged() {
        UnityPlayer.UnitySendMessage(gameObjectName, "OnAndroidMidiDevicesChanged", "");
    }

    private void closeCurrent() {
        if (outputPort != null) {
            outputPort.disconnect(receiver);
            try { outputPort.close(); } catch (IOException ignored) { }
            outputPort = null;
        }
        if (openDevice != null) {
            try { openDevice.close(); } catch (IOException ignored) { }
            openDevice = null;
        }
        if (!openDeviceName.isEmpty()) connection(false, openDeviceName);
        openDeviceName = "";
        runningStatus = 0;
        data1 = -1;
    }

    private static boolean hasOutputPort(MidiDeviceInfo info) {
        for (MidiDeviceInfo.PortInfo port : info.getPorts())
            if (port.getType() == MidiDeviceInfo.PortInfo.TYPE_OUTPUT) return true;
        return false;
    }

    private static String deviceName(MidiDeviceInfo info) {
        String product = info.getProperties().getString(MidiDeviceInfo.PROPERTY_PRODUCT);
        String name = info.getProperties().getString(MidiDeviceInfo.PROPERTY_NAME);
        String manufacturer = info.getProperties().getString(MidiDeviceInfo.PROPERTY_MANUFACTURER);
        String chosen = product != null ? product : name != null ? name : "MIDI " + info.getId();
        return manufacturer != null ? manufacturer + " " + chosen : chosen;
    }

    private void registerDeviceCallback(Context context) {
        if (Build.VERSION.SDK_INT >= 33) {
            midiManager.registerDeviceCallback(
                    MidiManager.TRANSPORT_MIDI_BYTE_STREAM, context.getMainExecutor(), deviceCallback);
        } else {
            registerLegacyDeviceCallback();
        }
    }

    @SuppressWarnings("deprecation")
    private void registerLegacyDeviceCallback() {
        midiManager.registerDeviceCallback(deviceCallback, mainHandler);
    }

    private MidiDeviceInfo[] midi1Devices() {
        if (Build.VERSION.SDK_INT >= 33) {
            return midiManager.getDevicesForTransport(MidiManager.TRANSPORT_MIDI_BYTE_STREAM)
                    .toArray(new MidiDeviceInfo[0]);
        }
        return legacyDevices();
    }

    @SuppressWarnings("deprecation")
    private MidiDeviceInfo[] legacyDevices() {
        return midiManager.getDevices();
    }

    private static String encodeName(String name) {
        return Base64.encodeToString(name.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
    }
}
