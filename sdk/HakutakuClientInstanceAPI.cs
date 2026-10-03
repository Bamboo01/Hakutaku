#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hakutaku.ClientModels;

namespace Hakutaku
{
    // The client API for one player, like PlayFabClientInstanceAPI. It holds its own
    // settings and session, so any number can run side by side; the simulator makes one
    // per mock player. A game with a single local player can use the static
    // HakutakuClientAPI instead, which wraps one of these.
    //
    // Every call comes in two forms, as in PlayFab's SDKs:
    //   XxxAsync(request)                  returns a HakutakuResult and never throws
    //   Xxx(request, onResult, onError)    the Unity-style callback version
    public class HakutakuClientInstanceAPI
    {
        public readonly HakutakuApiSettings apiSettings;
        public readonly HakutakuAuthenticationContext authenticationContext;

        public HakutakuClientInstanceAPI()
            : this(new HakutakuApiSettings(), new HakutakuAuthenticationContext()) { }

        public HakutakuClientInstanceAPI(HakutakuApiSettings settings)
            : this(settings, new HakutakuAuthenticationContext()) { }

        public HakutakuClientInstanceAPI(HakutakuApiSettings settings, HakutakuAuthenticationContext context)
        {
            apiSettings = settings;
            authenticationContext = context;
        }

        public bool IsClientLoggedIn() => authenticationContext.IsClientLoggedIn();

        public void ForgetAllCredentials() => authenticationContext.ForgetAllCredentials();

        // ---- Account: login and identities -------------------------------------------

        public async Task<HakutakuResult<LoginResult>> RegisterGuestAsync(RegisterGuestRequest request) =>
            Map(await Call<SessionWire>("POST", Routes.Register, new { hardwareId = request.HardwareId, displayName = request.DisplayName }, false).ConfigureAwait(false),
                StartSession);

        public async Task<HakutakuResult<LoginResult>> LoginWithDeviceTokenAsync(LoginWithDeviceTokenRequest request) =>
            Map(await Call<SessionWire>("POST", Routes.LoginDevice, new { deviceToken = request.DeviceToken }, false).ConfigureAwait(false),
                StartSession);

        public async Task<HakutakuResult<LoginResult>> LoginWithEmailAddressAsync(LoginWithEmailAddressRequest request) =>
            Map(await Call<SessionWire>("POST", Routes.LoginEmail, new { email = request.Email, password = request.Password }, false).ConfigureAwait(false),
                StartSession);

        public async Task<HakutakuResult<UpdateUserTitleDisplayNameResult>> UpdateUserTitleDisplayNameAsync(UpdateUserTitleDisplayNameRequest request) =>
            Map(await Call<DisplayNameWire>("POST", Routes.DisplayName, new { displayName = request.DisplayName }, true).ConfigureAwait(false),
                w => new UpdateUserTitleDisplayNameResult { DisplayName = w.DisplayName ?? "" });

        public async Task<HakutakuResult<LinkEmailAddressResult>> LinkEmailAddressAsync(LinkEmailAddressRequest request) =>
            Map(await Call<LinkEmailWire>("POST", Routes.LinkEmail, new { email = request.Email, password = request.Password }, true).ConfigureAwait(false),
                w => new LinkEmailAddressResult { Email = w.Email ?? "", Verified = w.Verified, VerificationSent = w.VerificationSent });

        public async Task<HakutakuResult<VerifyEmailResult>> VerifyEmailAsync(VerifyEmailRequest request) =>
            Map(await Call<VerifiedWire>("POST", Routes.VerifyEmail, new { code = request.Code }, true).ConfigureAwait(false),
                w => new VerifyEmailResult { Verified = w.Verified, DeviceTokenRemoved = w.DeviceTokenRemoved });

        public async Task<HakutakuResult<ResendVerificationEmailResult>> ResendVerificationEmailAsync(ResendVerificationEmailRequest request) =>
            Map(await Call<SentWire>("POST", Routes.ResendEmail, null, true).ConfigureAwait(false),
                w => new ResendVerificationEmailResult { Sent = w.Sent });

        public async Task<HakutakuResult<SendAccountRecoveryEmailResult>> SendAccountRecoveryEmailAsync(SendAccountRecoveryEmailRequest request) =>
            Map(await Call<OkWire>("POST", Routes.ForgotPassword, new { email = request.Email }, false).ConfigureAwait(false),
                _ => new SendAccountRecoveryEmailResult());

        // On success the server revokes every session of the player that email belongs to.
        // That isn't necessarily the player this client is logged in as, so the stored
        // session is left alone; if it was theirs, the next call gets a 401.
        public async Task<HakutakuResult<ResetPasswordResult>> ResetPasswordAsync(ResetPasswordRequest request) =>
            Map(await Call<OkWire>("POST", Routes.ResetPassword, new { email = request.Email, code = request.Code, newPassword = request.NewPassword }, false).ConfigureAwait(false),
                _ => new ResetPasswordResult());

        // ---- Game data -----------------------------------------------------------------
        // These routes are admin-only on the server for now, so a player session gets a
        // 401 (and the public domain a 404) until the player API opens them up; see
        // TODO.md item 2. The calls are here so games can be written against them already.

        public async Task<HakutakuResult<WriteEventResponse>> WritePlayerEventAsync(WriteClientPlayerEventRequest request)
        {
            var body = new
            {
                playerId = authenticationContext.PlayerId,
                eventType = request.EventName,
                data = HakutakuHttp.Serialize(request.Body ?? new Dictionary<string, object>()),
            };
            return Map(await Call<EventWire>("POST", Routes.Events, body, true).ConfigureAwait(false),
                w => new WriteEventResponse { EventId = w.Id ?? "" });
        }

        // Returns every character the server sends back. Today that is all players'
        // characters, because the route is the admin's list.
        public async Task<HakutakuResult<ListUsersCharactersResult>> GetAllUsersCharactersAsync(ListUsersCharactersRequest request) =>
            Map(await Call<List<CharacterWire>>("GET", Routes.Characters, null, true).ConfigureAwait(false),
                w => new ListUsersCharactersResult { Characters = w.Select(ToCharacterResult).ToList() });

        public async Task<HakutakuResult<GrantCharacterToUserResult>> GrantCharacterToUserAsync(GrantCharacterToUserRequest request)
        {
            var body = new { playerId = authenticationContext.PlayerId, name = request.CharacterName };
            return Map(await Call<CharacterWire>("POST", Routes.Characters, body, true).ConfigureAwait(false),
                w => new GrantCharacterToUserResult { CharacterId = w.Id ?? "" });
        }

        // For routes this SDK doesn't wrap. Sends the session token if there is one and
        // returns the response body as text. The simulator uses it to check that admin
        // routes turn a player token away; a game shouldn't need it.
        public Task<HakutakuResult<string>> SendRawRequestAsync(string method, string path, string? jsonBody = null) =>
            HakutakuHttp.SendRawAsync(apiSettings, method, path, jsonBody, authenticationContext.SessionTicket);

        // ---- Callback versions ----------------------------------------------------------

        public void RegisterGuest(RegisterGuestRequest request, Action<LoginResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(RegisterGuestAsync(request), resultCallback, errorCallback);

        public void LoginWithDeviceToken(LoginWithDeviceTokenRequest request, Action<LoginResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(LoginWithDeviceTokenAsync(request), resultCallback, errorCallback);

        public void LoginWithEmailAddress(LoginWithEmailAddressRequest request, Action<LoginResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(LoginWithEmailAddressAsync(request), resultCallback, errorCallback);

        public void UpdateUserTitleDisplayName(UpdateUserTitleDisplayNameRequest request, Action<UpdateUserTitleDisplayNameResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(UpdateUserTitleDisplayNameAsync(request), resultCallback, errorCallback);

        public void LinkEmailAddress(LinkEmailAddressRequest request, Action<LinkEmailAddressResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(LinkEmailAddressAsync(request), resultCallback, errorCallback);

        public void VerifyEmail(VerifyEmailRequest request, Action<VerifyEmailResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(VerifyEmailAsync(request), resultCallback, errorCallback);

        public void ResendVerificationEmail(ResendVerificationEmailRequest request, Action<ResendVerificationEmailResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(ResendVerificationEmailAsync(request), resultCallback, errorCallback);

        public void SendAccountRecoveryEmail(SendAccountRecoveryEmailRequest request, Action<SendAccountRecoveryEmailResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(SendAccountRecoveryEmailAsync(request), resultCallback, errorCallback);

        public void ResetPassword(ResetPasswordRequest request, Action<ResetPasswordResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(ResetPasswordAsync(request), resultCallback, errorCallback);

        public void WritePlayerEvent(WriteClientPlayerEventRequest request, Action<WriteEventResponse>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(WritePlayerEventAsync(request), resultCallback, errorCallback);

        public void GetAllUsersCharacters(ListUsersCharactersRequest request, Action<ListUsersCharactersResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(GetAllUsersCharactersAsync(request), resultCallback, errorCallback);

        public void GrantCharacterToUser(GrantCharacterToUserRequest request, Action<GrantCharacterToUserResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Dispatch(GrantCharacterToUserAsync(request), resultCallback, errorCallback);

        // ---- Plumbing -------------------------------------------------------------------

        // The await resumes on the caller's SynchronizationContext, so when a Unity
        // MonoBehaviour makes the call, the callback runs on the main thread, as PlayFab's
        // do. Everything inside the SDK uses ConfigureAwait(false), which doesn't change that.
        static async void Dispatch<T>(Task<HakutakuResult<T>> call, Action<T>? resultCallback, Action<HakutakuError>? errorCallback)
            where T : class
        {
            var result = await call;
            if (result.Error != null) errorCallback?.Invoke(result.Error);
            else resultCallback?.Invoke(result.Result!);
        }

        // Session calls fail here, without a request, when there is no session to send,
        // like PlayFab's "Must be logged in to call this method".
        Task<HakutakuResult<TWire>> Call<TWire>(string method, string path, object? body, bool needsSession) where TWire : class
        {
            if (needsSession && !IsClientLoggedIn())
                return Task.FromResult(HakutakuHttp.Fail<TWire>(path, HakutakuErrorCode.NotLoggedIn, "must be logged in to call this method"));
            return HakutakuHttp.SendAsync<TWire>(apiSettings, method, path, body, needsSession ? authenticationContext.SessionTicket : null);
        }

        static HakutakuResult<TResult> Map<TWire, TResult>(HakutakuResult<TWire> wire, Func<TWire, TResult> map)
            where TWire : class
            where TResult : class =>
            wire.Error != null
                ? new HakutakuResult<TResult> { Error = wire.Error }
                : new HakutakuResult<TResult> { Result = map(wire.Result!) };

        LoginResult StartSession(SessionWire session)
        {
            authenticationContext.PlayerId = session.Id;
            authenticationContext.SessionTicket = session.Token;
            authenticationContext.SessionExpiration = session.ExpiresAt;
            return new LoginResult
            {
                PlayerId = session.Id ?? "",
                DisplayName = session.DisplayName,
                SessionTicket = session.Token ?? "",
                SessionExpiration = session.ExpiresAt,
                Xp = session.Xp,
                DeviceToken = session.DeviceToken,
            };
        }

        static CharacterResult ToCharacterResult(CharacterWire c) =>
            new CharacterResult { CharacterId = c.Id ?? "", CharacterName = c.Name ?? "" };
    }
}
