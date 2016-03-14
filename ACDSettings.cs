
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using FarNet.Settings;
namespace FarNet.ACD
{
	/// <summary>
	/// Settings set 1.
	/// It uses some standard settings (supported by Visual Studio designer)
	/// and shows how to use various settings property attributes.
	/// </summary>
	[SettingsProvider(typeof(ModuleSettingsProvider))]
	public class ACDSettings : ModuleSettings
	{
		#region [Default]
		/// <summary>
		/// The only settings instance.
		/// Normally settings are created once, when needed.
		/// </summary>
		/// <remarks>
		/// Use <see cref="SettingsBase.Synchronized"/> in multithreaded scenarious, see <see cref="Settings2._Default"/>.
		/// </remarks>
		static readonly ACDSettings _Default = new ACDSettings();
		/// <summary>
		/// Gets the public access to the settings instance.
		/// It is used for example by the core in order to open the settings panel.
		/// </summary>
		public static ACDSettings Default { get { return _Default; } }
		#endregion
		#region [Save]
		/// <summary>
		/// Override this method to perform data validation.
		/// Throw on errors. Call the base on success.
		/// </summary>
		public override void Save()
		{
            /*
            if (IntLocal < 0)
				throw new ModuleException("Negative 'IntLocal' is invalid.");

			if (IntRoaming < 0)
				throw new ModuleException("Negative 'IntRoaming' is invalid.");
            */
			base.Save();
		}
		#endregion
		#region [String]
		/// <summary>
		/// Client ID.
		/// </summary>
		[UserScopedSetting]
		public string ClientId
        {
			get { return (string)this["ClientId"]; }
			set { this["ClientId"] = value; }
		}
        /// <summary>
        /// Client Secret.
        /// </summary>
        [UserScopedSetting]
        public string ClientSecret
        {
            get { return (string)this["ClientSecret"]; }
            set { this["ClientSecret"] = value; }
        }
        /// <summary>
        /// AuthToken.
        /// </summary>
		//[Browsable(false)]
        [UserScopedSetting]
        public string AuthToken
        {
            get { return (string)this["AuthToken"]; }
            set { this["AuthToken"] = value; }
        }
        /// <summary>
        /// AuthRenewToken.
        /// </summary>
		//[Browsable(false)]
        [UserScopedSetting]
        public string AuthRenewToken
        {
            get { return (string)this["AuthRenewToken"]; }
            set { this["AuthRenewToken"] = value; }
        }
        #endregion
        #region [DateTime]
        /// <summary>
        /// AuthTokenExpiration.
        /// </summary>
		//[Browsable(false)]
        [UserScopedSetting]
        [DefaultSettingValue("2000-11-22")]
        public DateTime AuthTokenExpiration
        {
            get { return (DateTime)this["AuthTokenExpiration"]; }
            set { this["AuthTokenExpiration"] = value; }
        }
        #endregion
    }
}
