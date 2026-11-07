package vn.networkadmin.client.application.remoteadministration;

public interface RemoteAdministrationPort {
    void lockScreen() throws Exception;
    void shutdown() throws Exception;
    void restart() throws Exception;
}
