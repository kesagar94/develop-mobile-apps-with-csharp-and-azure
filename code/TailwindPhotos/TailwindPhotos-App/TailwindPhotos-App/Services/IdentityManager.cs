﻿using Microsoft.AppCenter.Crashes;
using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Tailwind.Photos.Services
{
    public class IdentityManager
    {
        private readonly string Tenant = "tailwinds.onmicrosoft.com";
        private readonly string Hostname = "tailwinds.b2clogin.com";
        private readonly string ApplicationId = "cbd3e5fb-b1c2-496e-b276-08b58de76c2a";
        private readonly string UserFlow = "B2C_1_Signin";
        private readonly string RedirectUri = "msal-tailwinds-photos://auth";
        private readonly string IOSKeyChainGroup = "com.microsoft.adalcache";

        private string AuthorityBase;
        private string Authority;
        private readonly string[] Scopes = {
            "https://tailwinds.onmicrosoft.com/api/Tailwinds.API"
        };

        /// <summary>
        /// Lazy initializer for the identity manager (do not touch)
        /// </summary>
        private static readonly Lazy<IdentityManager> lazy
            = new Lazy<IdentityManager>(() => new IdentityManager());

        private static IPublicClientApplication idp = null;
        public static object ParentWindow { get; set; }

        /// <summary>
        /// Obtain a reference to the identity manager.
        /// </summary>
        public static IdentityManager Instance
        {
            get { return lazy.Value; }
        }

        private IdentityManager()
        {
            AuthorityBase = $"https://{Hostname}/tfp/{Tenant}/";
            Authority = $"{AuthorityBase}{UserFlow}";

            idp = PublicClientApplicationBuilder
                .Create(ApplicationId)
                .WithB2CAuthority(Authority)
                .WithIosKeychainSecurityGroup(IOSKeyChainGroup)
                .WithRedirectUri(RedirectUri)
                .Build();
        }

        public AuthenticationResult LastAuthenticationResult
        {
            get;
            private set;
        }

        public AuthenticatedUser AuthenticatedUser
        {
            get;
            private set;
        }

        public bool IsAuthenticated
        {
            get;
            private set;
        }

        private async Task<IAccount> GetAccountByPolicy(string policy)
        {
            var accounts = await idp.GetAccountsAsync();
            foreach (var account in accounts)
            {
                string userIdentifier = account.HomeAccountId.ObjectId.Split('.')[0];
                if (userIdentifier.EndsWith(policy.ToLower())) return account;
            }
            return null;
        }

        public async Task SilentlySignIn()
        {
            try
            {
                var account = await GetAccountByPolicy(UserFlow);
                var result = await idp.AcquireTokenSilent(Scopes, account)
                    .WithB2CAuthority(Authority)
                    .ExecuteAsync();
                LastAuthenticationResult = result;
                AuthenticatedUser = new AuthenticatedUser(result.IdToken);
                IsAuthenticated = true;
            }
            catch (MsalUiRequiredException)
            {
                // Ignore this error - it's used to fall-through
            }
        }

        public async Task InteractivelySignin(object sender = null)
        {
            await SilentlySignIn();
            if (IsAuthenticated)
            {
                return;
            }

            // If we can silently acquire the token, then this is never reached.
            // If we get here, we need to pop up a UI
            try
            {
                var window = (sender == null) ? IdentityManager.ParentWindow : sender;
                var account = await GetAccountByPolicy(UserFlow);
                var uiResult = await idp
                    .AcquireTokenInteractive(Scopes)
                    .WithAccount(account)
                    .WithParentActivityOrWindow(window)
                    .ExecuteAsync();
                LastAuthenticationResult = uiResult;
                AuthenticatedUser = new AuthenticatedUser(uiResult.IdToken);
                IsAuthenticated = true;
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }
        }

        public async void Signout()
        {
            try
            {
                var accounts = await idp.GetAccountsAsync();
                while (accounts.Any())
                {
                    await idp.RemoveAsync(accounts.FirstOrDefault());
                    accounts = await idp.GetAccountsAsync();
                }
                LastAuthenticationResult = null;
                IsAuthenticated = false;
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }
        }
    }
}
