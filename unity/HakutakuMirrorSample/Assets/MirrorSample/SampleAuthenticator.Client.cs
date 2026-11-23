// Ported from Sundered Frames' SFAuthenticator.Client.cs (Gam300, feature/hakutaku-integration).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace HakutakuMirrorSample
{
    public partial class SampleAuthenticator
    {
        public override void OnStartClient()
        {
            base.OnStartClient();
            // register a handler for the authentication response we expect from the server
            NetworkClient.RegisterHandler<SampleAuthResponse>(OnClientReceiveAuthResponse, false);

            // Start logging in now, while the transport connects, so the ticket is usually ready by the time
            // OnClientAuthenticate needs it
            _ = SamplePlayerLogin.GetTicketAsync();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            // unregister the handler for the authentication response
            NetworkClient.UnregisterHandler<SampleAuthResponse>();
        }

        public override async void OnClientAuthenticate()
        {
            base.OnClientAuthenticate();
            var conn = NetworkClient.connection;

            string ticket = await SamplePlayerLogin.GetTicketAsync();

            // The player may have cancelled or dropped while the login was running
            if (!NetworkClient.active || NetworkClient.connection != conn)
                return;

            NetworkClient.Send(new SampleAuthRequest { ticket = ticket });
        }
        
        void OnClientReceiveAuthResponse(SampleAuthResponse res)
        {
            
            if (res.ok)
                ClientAccept();
            else
            {
                SamplePlayerLogin.ForgetSession();
                ClientReject();
            }
        }
    }
}