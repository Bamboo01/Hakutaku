#nullable enable

using System;
using System.Threading.Tasks;
using Hakutaku.ClientModels;

namespace Hakutaku
{
    // The static entry point, like PlayFabClientAPI: one player per process, configured
    // through HakutakuSettings.staticSettings and staticPlayer. This is what a game uses:
    //
    //     HakutakuSettings.staticSettings.ServerUrl = "https://51.79.242.169.nip.io";
    //     HakutakuClientAPI.LoginWithDeviceID(
    //         new LoginWithDeviceIDRequest { DeviceId = SystemInfo.deviceUniqueIdentifier },
    //         result => Debug.Log("Logged in as " + result.PlayerId),
    //         error => Debug.LogError(error.GenerateErrorReport()));
    //
    // Anything that needs several players at once uses HakutakuClientInstanceAPI directly.
    public static class HakutakuClientAPI
    {
        static readonly HakutakuClientInstanceAPI Instance =
            new HakutakuClientInstanceAPI(HakutakuSettings.staticSettings, HakutakuSettings.staticPlayer);

        public static bool IsClientLoggedIn() => Instance.IsClientLoggedIn();

        public static void ForgetAllCredentials() => Instance.ForgetAllCredentials();

        public static void LoginWithDeviceID(LoginWithDeviceIDRequest request, Action<LoginResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.LoginWithDeviceID(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<LoginResult>> LoginWithDeviceIDAsync(LoginWithDeviceIDRequest request) =>
            Instance.LoginWithDeviceIDAsync(request);

        public static void LoginWithEmailAddress(LoginWithEmailAddressRequest request, Action<LoginResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.LoginWithEmailAddress(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<LoginResult>> LoginWithEmailAddressAsync(LoginWithEmailAddressRequest request) =>
            Instance.LoginWithEmailAddressAsync(request);

        public static void UpdateUserTitleDisplayName(UpdateUserTitleDisplayNameRequest request, Action<UpdateUserTitleDisplayNameResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.UpdateUserTitleDisplayName(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<UpdateUserTitleDisplayNameResult>> UpdateUserTitleDisplayNameAsync(UpdateUserTitleDisplayNameRequest request) =>
            Instance.UpdateUserTitleDisplayNameAsync(request);

        public static void LinkEmailAddress(LinkEmailAddressRequest request, Action<LinkEmailAddressResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.LinkEmailAddress(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<LinkEmailAddressResult>> LinkEmailAddressAsync(LinkEmailAddressRequest request) =>
            Instance.LinkEmailAddressAsync(request);

        public static void VerifyEmail(VerifyEmailRequest request, Action<VerifyEmailResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.VerifyEmail(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<VerifyEmailResult>> VerifyEmailAsync(VerifyEmailRequest request) =>
            Instance.VerifyEmailAsync(request);

        public static void ResendVerificationEmail(ResendVerificationEmailRequest request, Action<ResendVerificationEmailResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.ResendVerificationEmail(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<ResendVerificationEmailResult>> ResendVerificationEmailAsync(ResendVerificationEmailRequest request) =>
            Instance.ResendVerificationEmailAsync(request);

        public static void SendAccountRecoveryEmail(SendAccountRecoveryEmailRequest request, Action<SendAccountRecoveryEmailResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.SendAccountRecoveryEmail(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<SendAccountRecoveryEmailResult>> SendAccountRecoveryEmailAsync(SendAccountRecoveryEmailRequest request) =>
            Instance.SendAccountRecoveryEmailAsync(request);

        public static void ResetPassword(ResetPasswordRequest request, Action<ResetPasswordResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.ResetPassword(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<ResetPasswordResult>> ResetPasswordAsync(ResetPasswordRequest request) =>
            Instance.ResetPasswordAsync(request);

        public static void WritePlayerEvent(WriteClientPlayerEventRequest request, Action<WriteEventResponse>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.WritePlayerEvent(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<WriteEventResponse>> WritePlayerEventAsync(WriteClientPlayerEventRequest request) =>
            Instance.WritePlayerEventAsync(request);

        public static void GetAllUsersCharacters(ListUsersCharactersRequest request, Action<ListUsersCharactersResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.GetAllUsersCharacters(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<ListUsersCharactersResult>> GetAllUsersCharactersAsync(ListUsersCharactersRequest request) =>
            Instance.GetAllUsersCharactersAsync(request);

        public static void GrantCharacterToUser(GrantCharacterToUserRequest request, Action<GrantCharacterToUserResult>? resultCallback, Action<HakutakuError>? errorCallback) =>
            Instance.GrantCharacterToUser(request, resultCallback, errorCallback);

        public static Task<HakutakuResult<GrantCharacterToUserResult>> GrantCharacterToUserAsync(GrantCharacterToUserRequest request) =>
            Instance.GrantCharacterToUserAsync(request);
    }
}
