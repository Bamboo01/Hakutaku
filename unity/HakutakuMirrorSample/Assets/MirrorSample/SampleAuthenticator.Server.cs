// Ported from Sundered Frames' SFAuthenticator.Server.cs (Gam300, feature/hakutaku-integration).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Mirror;
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
using Hakutaku;
using Hakutaku.ServerModels;
using Hakutaku.Unity;
#endif

namespace HakutakuMirrorSample
{
    // Checks each player with Hakutaku as they join, without blocking the tick:
    //   1. AuthenticateSessionTicket: who they are. An invalid ticket is turned away.
    //   2. GetUserReadOnlyData(Xp): their total. A missing key is 0.
    // Anything else (Hakutaku down, a timeout, no ticket, a refused server key) and they join offline: XP starts at
    // 0 and is never written, because a failed read isn't 0 XP.
    //
    // The Hakutaku calls only exist in the editor and server builds. A client build has no server SDK or key.
    public partial class SampleAuthenticator
    {
        const int AuthTimeoutMs = 2000;
        const string XpKey = "Xp";

        // Long enough for the refusal to reach the client before the connection closes
        const float RejectDisconnectDelay = 1f;

        // Waiting on Hakutaku, or on the delayed disconnect. A second request from these is ignored.
        readonly HashSet<NetworkConnectionToClient> authenticating = new HashSet<NetworkConnectionToClient>();

        // How often waiting XP totals are looked at. Each player is still written at most every 30 s.
        const float XpSaveIntervalSeconds = 5f;

#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
        HakutakuServerInstanceAPI backend;
        SampleXpSaver xpSaver;
        Coroutine xpSaveLoop;
#endif

        public override void OnStartServer()
        {
            base.OnStartServer();
            // register a handler for the authentication request we expect from client
            NetworkServer.RegisterHandler<SampleAuthRequest>(OnServerReceiveAuth, false);
            CreateBackend();
            StartXpSaving();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            // unregister the handler for the authentication request
            NetworkServer.UnregisterHandler<SampleAuthRequest>();
            authenticating.Clear();
            StopXpSaving();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            base.OnServerAuthenticate(conn);
        }

        void CreateBackend()
        {
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
            string url = HakutakuServerSettings.ResolveUrl();

            string key = HakutakuServerSettings.ResolveKey();
            if (key == null)
                Debug.LogWarning($"[SampleAuthenticator] No {HakutakuServerSettings.KeyEnvironmentVariable}: every player will join offline and no XP will be saved.");

            backend = new HakutakuServerInstanceAPI(url, key, TimeSpan.FromMilliseconds(AuthTimeoutMs));
#else
            Debug.LogWarning("[SampleAuthenticator] This build has no server SDK: every player will join offline.");
#endif
        }

        void OnServerReceiveAuth(NetworkConnectionToClient conn, SampleAuthRequest req)
        {
            if (conn.isAuthenticated || !authenticating.Add(conn))
                return;

            AuthenticateAsync(conn, req.ticket);
        }

        // What Hakutaku said so far. A timeout keeps whatever had already arrived.
        sealed class AuthLookup
        {
            public bool TicketRejected;
            public string PlayerId;
            public string DisplayName;
            public long Xp;
            public bool XpLoaded;
        }

        async void AuthenticateAsync(NetworkConnectionToClient conn, string ticket)
        {
            var lookup = new AuthLookup();

#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
            if (!string.IsNullOrEmpty(ticket))
            {
                Task work = LookUpAsync(ticket, lookup);
                if (await Task.WhenAny(work, Task.Delay(AuthTimeoutMs)) != work)
                    Debug.LogWarning($"[SampleAuthenticator] Hakutaku didn't answer within {AuthTimeoutMs} ms; connection {conn.connectionId} joins offline.");
            }
#else
            await Task.CompletedTask;
#endif

            // The player may have left, or the server stopped, while we waited
            if (!IsStillConnected(conn))
            {
                authenticating.Remove(conn);
                return;
            }

            if (lookup.TicketRejected)
            {
                conn.Send(new SampleAuthResponse { ok = false, reason = SampleAuthRejectReason.InvalidTicket });
                StartCoroutine(RejectAfterDelay(conn));
                return;
            }

            // Read once here: a call that outlasted the timeout must not change this player afterwards
            var data = new SamplePlayerAuthData
            {
                PlayerId = lookup.PlayerId,
                DisplayName = lookup.DisplayName,
                Xp = lookup.XpLoaded ? lookup.Xp : 0,
                IsOnline = lookup.XpLoaded
            };

            if (data.PlayerId != null)
            {
                UseUnsavedXp(data);
                KickOlderConnections(conn, data);
            }

            conn.authenticationData = data;
            authenticating.Remove(conn);

            Debug.Log(data.IsOnline
                ? $"[SampleAuthenticator] Player {data.PlayerId} joined with {data.Xp} XP."
                : $"[SampleAuthenticator] Connection {conn.connectionId} joined offline; its XP won't be saved.");

            conn.Send(new SampleAuthResponse { ok = true });
            ServerAccept(conn);
        }

#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
        // Never throws. Runs on the main thread between its awaits.
        async Task LookUpAsync(string ticket, AuthLookup lookup)
        {
            try
            {
                var auth = await backend.AuthenticateSessionTicketAsync(
                    new AuthenticateSessionTicketRequest { SessionTicket = ticket });

                if (auth.Error != null)
                {
                    if (auth.Error.Error == HakutakuServerErrorCode.InvalidSessionTicket)
                        lookup.TicketRejected = true;
                    else
                        Debug.LogWarning($"[SampleAuthenticator] Couldn't check a ticket: {auth.Error.Error}, {auth.Error.ErrorMessage}");
                    return;
                }

                lookup.PlayerId = auth.Result.PlayerId;
                lookup.DisplayName = auth.Result.DisplayName;

                var read = await backend.GetUserReadOnlyDataAsync(new GetUserDataRequest
                {
                    PlayerId = auth.Result.PlayerId,
                    Keys = new List<string> { XpKey }
                });

                if (read.Error != null)
                {
                    Debug.LogWarning($"[SampleAuthenticator] Couldn't read XP for {auth.Result.PlayerId}: {read.Error.Error}, {read.Error.ErrorMessage}");
                    return;
                }

                long xp = 0;
                if (read.Result.Data.TryGetValue(XpKey, out string stored)
                    && (!long.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out xp)))
                {
                    // Treated like a failed read, so a value we can't understand is never overwritten
                    Debug.LogError($"[SampleAuthenticator] Player {auth.Result.PlayerId} has an invalid {XpKey} value; joining offline.");
                    return;
                }

                lookup.Xp = xp;
                lookup.XpLoaded = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SampleAuthenticator] Hakutaku lookup failed: {e.GetType().Name}: {e.Message}");
            }
        }
#endif

        // One connection per player. The older one's total is newer than anything stored, so the new one takes it.
        void KickOlderConnections(NetworkConnectionToClient conn, SamplePlayerAuthData data)
        {
            foreach (var other in NetworkServer.connections.Values.ToList())
            {
                if (other == conn || !(other.authenticationData is SamplePlayerAuthData otherData) || otherData.PlayerId != data.PlayerId)
                    continue;

                if (otherData.IsOnline)
                {
                    data.Xp = otherData.Xp;
                    data.IsOnline = true;

                    // The new connection owns this player's total now, so the old one must never write it
                    otherData.IsOnline = false;
                }

                Debug.Log($"[SampleAuthenticator] Player {data.PlayerId} logged in again; closing connection {other.connectionId}.");
                other.Disconnect();
            }
        }

        // ---- Saving XP ----

        // XP still on its way to Hakutaku is newer than what was just read from it
        void UseUnsavedXp(SamplePlayerAuthData data)
        {
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
            if (xpSaver != null && xpSaver.TryGetUnsaved(data.PlayerId, out long unsaved))
            {
                data.Xp = unsaved;
                data.IsOnline = true;
            }
#endif
        }

        void StartXpSaving()
        {
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
            xpSaver = new SampleXpSaver(WriteXpAsync, () => Time.realtimeSinceStartupAsDouble);
            SamplePlayerXp.Saver = xpSaver;
            xpSaveLoop = StartCoroutine(XpSaveLoop());
#endif
        }

        void StopXpSaving()
        {
#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
            if (xpSaver == null)
                return;

            if (xpSaveLoop != null)
                StopCoroutine(xpSaveLoop);
            xpSaveLoop = null;

            // Mirror doesn't report the shutdown's disconnects, so this is the last chance to send them
            xpSaver.FlushAll();
            if (xpSaver.UnsavedPlayers > 0)
                Debug.Log($"[SampleAuthenticator] Server stopping with XP still being saved for {xpSaver.UnsavedPlayers} player(s).");

            SamplePlayerXp.Saver = null;
            xpSaver = null;
#endif
        }

#if UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER
        IEnumerator XpSaveLoop()
        {
            var wait = new WaitForSecondsRealtime(XpSaveIntervalSeconds);
            while (true)
            {
                yield return wait;
                xpSaver?.Tick();
            }
        }

        // What Gam300 does on ServerPlayerDisconnected
        public void ServerPlayerLeft(NetworkConnectionToClient conn)
        {
            if (conn.authenticationData is SamplePlayerAuthData data && data.IsOnline)
                xpSaver?.Flush(data.PlayerId);
        }

        // What Gam300 does on entering POST_GAME
        public void ServerMatchEnded() => xpSaver?.FlushAll();

        async Task<bool> WriteXpAsync(string playerId, long total)
        {
            var result = await backend.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest
            {
                PlayerId = playerId,
                Data = new Dictionary<string, string> { [XpKey] = total.ToString(CultureInfo.InvariantCulture) }
            });

            if (result.Error == null)
                return true;

            Debug.LogWarning($"[SampleAuthenticator] Couldn't save XP for {playerId}, will retry: {result.Error.Error}, {result.Error.ErrorMessage}");
            return false;
        }
#endif

        static bool IsStillConnected(NetworkConnectionToClient conn) =>
            NetworkServer.active
            && NetworkServer.connections.TryGetValue(conn.connectionId, out var current)
            && current == conn;

        IEnumerator RejectAfterDelay(NetworkConnectionToClient conn)
        {
            yield return new WaitForSecondsRealtime(RejectDisconnectDelay);

            authenticating.Remove(conn);
            if (IsStillConnected(conn))
                ServerReject(conn);
        }
    }
}
