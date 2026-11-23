using Mirror;
using UnityEngine;

namespace HakutakuMirrorSample
{
    // The smallest Mirror game that shows a Hakutaku login and saved XP. There are no scenes, prefabs or players:
    // an authenticated connection is enough, and the server tells each client its own status.
    public class SampleNetworkManager : NetworkManager
    {
        public SampleStatusMessage? ClientStatus { get; private set; }

        SampleAuthenticator Auth => (SampleAuthenticator)authenticator;

        public override void OnStartClient()
        {
            base.OnStartClient();
            ClientStatus = null;
            NetworkClient.RegisterHandler<SampleStatusMessage>(OnClientStatus);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            NetworkClient.UnregisterHandler<SampleStatusMessage>();
            ClientStatus = null;
            Debug.Log("[Sample] client disconnected");
        }

        // Runs once the authenticator has accepted the connection
        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            ServerSendStatus(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Auth.ServerPlayerLeft(conn);
            if (conn.authenticationData is SamplePlayerAuthData data)
                Debug.Log($"[Sample] server: player {data.PlayerId ?? "(offline)"} left with {data.Xp} XP");
            base.OnServerDisconnect(conn);
        }

        // What a game's gameplay code would do when a player earns XP
        public void ServerAward(NetworkConnectionToClient conn, long amount)
        {
            if (!SamplePlayerXp.Award(conn, amount))
                return;

            var data = (SamplePlayerAuthData)conn.authenticationData;
            Debug.Log($"[Sample] server: player {data.PlayerId ?? "(offline)"} total {data.Xp} XP");
            ServerSendStatus(conn);
        }

        public void ServerEndMatch()
        {
            Debug.Log("[Sample] server: match ended, saving XP");
            Auth.ServerMatchEnded();
        }

        void ServerSendStatus(NetworkConnectionToClient conn)
        {
            if (conn.authenticationData is SamplePlayerAuthData data)
                conn.Send(new SampleStatusMessage
                {
                    playerId = data.PlayerId ?? "",
                    displayName = data.DisplayName ?? "",
                    xp = data.Xp,
                    online = data.IsOnline
                });
        }

        void OnClientStatus(SampleStatusMessage status)
        {
            ClientStatus = status;
            Debug.Log($"[Sample] client: player {status.playerId} xp {status.xp} online {status.online}");
        }
    }
}
