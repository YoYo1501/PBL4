package vn.networkadmin.client.application.remoteadministration;

public interface RemoteAdministrationClientView {
    void showCountdown(String command, int seconds, Runnable cancel);
    void clearCountdown(String status);
}
