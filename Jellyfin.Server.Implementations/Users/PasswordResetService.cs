using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;

namespace MulletaFlix.Server.Implementations.Users
{
    internal sealed class PasswordResetService
    {
        private readonly IReadOnlyCollection<IPasswordResetProvider> _passwordResetProviders;
        private readonly IPasswordResetProvider _defaultPasswordResetProvider;
        private readonly UserManager _userManager;

        public PasswordResetService(
            IReadOnlyCollection<IPasswordResetProvider> passwordResetProviders,
            IPasswordResetProvider defaultPasswordResetProvider,
            UserManager userManager)
        {
            _passwordResetProviders = passwordResetProviders;
            _defaultPasswordResetProvider = defaultPasswordResetProvider;
            _userManager = userManager;
        }

        public async Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork)
        {
            var user = string.IsNullOrWhiteSpace(enteredUsername) ? null : _userManager.GetUserByName(enteredUsername);
            var passwordResetProvider = GetPasswordResetProvider(user);

            var result = await passwordResetProvider
                .StartForgotPasswordProcess(user, enteredUsername, isInNetwork)
                .ConfigureAwait(false);

            if (user is not null && isInNetwork)
            {
                await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }

            return result;
        }

        public async Task<PinRedeemResult> RedeemPasswordResetPin(string pin)
        {
            foreach (var provider in _passwordResetProviders)
            {
                var result = await provider.RedeemPasswordResetPin(pin).ConfigureAwait(false);

                if (result.Success)
                {
                    return result;
                }
            }

            return new PinRedeemResult();
        }

        public NameIdPair[] GetPasswordResetProviders()
        {
            return _passwordResetProviders
                .Where(provider => provider.IsEnabled)
                .OrderBy(i => i is DefaultPasswordResetProvider ? 0 : 1)
                .ThenBy(i => i.Name)
                .Select(i => new NameIdPair
                {
                    Name = i.Name,
                    Id = i.GetType().FullName
                })
                .ToArray();
        }

        public IPasswordResetProvider GetPasswordResetProvider(User? user)
        {
            if (user is null)
            {
                return _defaultPasswordResetProvider;
            }

            return GetPasswordResetProviders(user)[0];
        }

        private IPasswordResetProvider[] GetPasswordResetProviders(User user)
        {
            var passwordResetProviderId = user.PasswordResetProviderId;
            var providers = _passwordResetProviders.Where(i => i.IsEnabled).ToArray();

            if (!string.IsNullOrEmpty(passwordResetProviderId))
            {
                providers = providers.Where(i =>
                        string.Equals(passwordResetProviderId, i.GetType().FullName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            if (providers.Length == 0)
            {
                providers = new IPasswordResetProvider[]
                {
                    _defaultPasswordResetProvider
                };
            }

            return providers;
        }
    }
}
