#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hakutaku.ServerModels;

namespace Hakutaku
{
    // The game server's API, like PlayFabServerInstanceAPI. It holds the server URL and this game
    // server's key, which the game server reads from its own environment, never from a build:
    //
    //   var server = new HakutakuServerInstanceAPI(url, Environment.GetEnvironmentVariable("HAKUTAKU_SERVER_KEY"));
    //
    // Every call comes in two forms, as in the client SDK:
    //   XxxAsync(request)                  returns a HakutakuServerResult and never throws
    //   Xxx(request, onResult, onError)    the Unity-style callback version
    public class HakutakuServerInstanceAPI
    {
        // The longest any call waits by default. A player waits on AuthenticateSessionTicket while
        // joining, so it is short: if Hakutaku is that slow, letting them in offline is better.
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        public readonly string ServerUrl;
        public readonly TimeSpan Timeout;
        readonly string? serverKey;

        // timeout is for each call, up to 30 seconds (the shared HTTP client's own limit)
        public HakutakuServerInstanceAPI(string serverUrl, string? serverKey, TimeSpan? timeout = null)
        {
            ServerUrl = serverUrl;
            this.serverKey = string.IsNullOrWhiteSpace(serverKey) ? null : serverKey!.Trim();
            Timeout = timeout ?? DefaultTimeout;
        }

        // ---- Authentication ---------------------------------------------------------------

        // Who the player joining with this ticket is. On InvalidSessionTicket turn them away; if
        // error.IsHakutakuUnavailable, Hakutaku is down and the game decides whether to let them in offline.
        public async Task<HakutakuServerResult<AuthenticateSessionTicketResult>> AuthenticateSessionTicketAsync(AuthenticateSessionTicketRequest request) =>
            Map(await Call<ValidateSessionWire>("POST", Routes.ValidateSession, new { ticket = request.SessionTicket }).ConfigureAwait(false),
                w => new AuthenticateSessionTicketResult { PlayerId = w.PlayerId ?? "", DisplayName = w.DisplayName });

        // ---- Player data (the ReadOnly tier: players can't change it, the game server can) -------

        public async Task<HakutakuServerResult<GetUserDataResult>> GetUserReadOnlyDataAsync(GetUserDataRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PlayerId))
                return Refuse<GetUserDataResult>(Routes.PlayerData("", ReadOnlyTier), "a PlayerId is required");

            var path = Routes.PlayerData(request.PlayerId, ReadOnlyTier);
            // no keys means every key, which is what the route does with no keys parameter. an empty one
            // would be refused, so it's left off rather than sent as keys=
            if (request.Keys != null && request.Keys.Count > 0)
                path += "&keys=" + Uri.EscapeDataString(string.Join(",", request.Keys));

            return Map(await Call<PlayerDataWire>("GET", path, null).ConfigureAwait(false),
                w => new GetUserDataResult { PlayerId = request.PlayerId, Data = w.Data ?? new Dictionary<string, string>() });
        }

        public async Task<HakutakuServerResult<UpdateUserDataResult>> UpdateUserReadOnlyDataAsync(UpdateUserDataRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PlayerId))
                return Refuse<UpdateUserDataResult>(Routes.PlayerData("", ReadOnlyTier), "a PlayerId is required");

            var body = new { data = request.Data, keysToRemove = request.KeysToRemove };
            return Map(await Call<PlayerDataUpdatedWire>("POST", Routes.PlayerData(request.PlayerId, ReadOnlyTier), body).ConfigureAwait(false),
                w => new UpdateUserDataResult { Updated = w.Updated });
        }

        // ---- Callback versions ----------------------------------------------------------------

        public void AuthenticateSessionTicket(AuthenticateSessionTicketRequest request, Action<AuthenticateSessionTicketResult>? resultCallback, Action<HakutakuServerError>? errorCallback) =>
            Dispatch(AuthenticateSessionTicketAsync(request), resultCallback, errorCallback);

        public void GetUserReadOnlyData(GetUserDataRequest request, Action<GetUserDataResult>? resultCallback, Action<HakutakuServerError>? errorCallback) =>
            Dispatch(GetUserReadOnlyDataAsync(request), resultCallback, errorCallback);

        public void UpdateUserReadOnlyData(UpdateUserDataRequest request, Action<UpdateUserDataResult>? resultCallback, Action<HakutakuServerError>? errorCallback) =>
            Dispatch(UpdateUserReadOnlyDataAsync(request), resultCallback, errorCallback);

        // ---- Plumbing -----------------------------------------------------------------------

        // the tier is sent by name, the route refuses numbers
        const string ReadOnlyTier = "ReadOnly";

        // the callback runs on the caller's SynchronizationContext, as in the client SDK
        static async void Dispatch<T>(Task<HakutakuServerResult<T>> call, Action<T>? resultCallback, Action<HakutakuServerError>? errorCallback)
            where T : class
        {
            var result = await call;
            if (result.Error != null) errorCallback?.Invoke(result.Error);
            else resultCallback?.Invoke(result.Result!);
        }

        // fails here, without a request, when there's no key to send
        Task<HakutakuResult<TWire>> Call<TWire>(string method, string path, object? body) where TWire : class
        {
            if (serverKey == null)
                return Task.FromResult(HakutakuHttp.Fail<TWire>(path, HakutakuErrorCode.NotLoggedIn, "no server key was given; set HAKUTAKU_SERVER_KEY on the game server"));
            return HakutakuHttp.SendAsync<TWire>(ServerUrl, method, path, body, null, serverKey, Timeout);
        }

        static HakutakuServerResult<T> Refuse<T>(string path, string message) where T : class =>
            new HakutakuServerResult<T>
            {
                Error = new HakutakuServerError { ApiEndpoint = path, Error = HakutakuServerErrorCode.InvalidParams, ErrorMessage = message },
            };

        static HakutakuServerResult<TResult> Map<TWire, TResult>(HakutakuResult<TWire> wire, Func<TWire, TResult> map)
            where TWire : class
            where TResult : class =>
            wire.Error != null
                ? new HakutakuServerResult<TResult> { Error = ToServerError(wire.Error) }
                : new HakutakuServerResult<TResult> { Result = map(wire.Result!) };

        // the shared code's error, in the server SDK's terms. the one split that matters most: a 401
        // carrying the invalid-ticket code is the player's ticket, any other 401 is this server's key
        static HakutakuServerError ToServerError(HakutakuError e) =>
            new HakutakuServerError
            {
                ApiEndpoint = e.ApiEndpoint,
                HttpCode = e.HttpCode,
                HttpStatus = e.HttpStatus,
                ErrorMessage = e.ErrorMessage,
                Error = e.Error switch
                {
                    HakutakuErrorCode.NotAuthenticated => e.ServerCode == ServerCodes.InvalidTicket
                        ? HakutakuServerErrorCode.InvalidSessionTicket
                        : HakutakuServerErrorCode.ServerKeyRejected,
                    HakutakuErrorCode.NotLoggedIn => HakutakuServerErrorCode.MissingServerKey,
                    HakutakuErrorCode.Timeout => HakutakuServerErrorCode.Timeout,
                    HakutakuErrorCode.ConnectionError => HakutakuServerErrorCode.ConnectionError,
                    HakutakuErrorCode.InternalServerError => HakutakuServerErrorCode.InternalServerError,
                    HakutakuErrorCode.ServiceUnavailable => HakutakuServerErrorCode.ServiceUnavailable,
                    HakutakuErrorCode.InvalidParams => HakutakuServerErrorCode.InvalidParams,
                    HakutakuErrorCode.NotFound => HakutakuServerErrorCode.NotFound,
                    HakutakuErrorCode.TooManyRequests => HakutakuServerErrorCode.TooManyRequests,
                    HakutakuErrorCode.JsonParseError => HakutakuServerErrorCode.JsonParseError,
                    _ => HakutakuServerErrorCode.Unknown,
                },
            };
    }
}
