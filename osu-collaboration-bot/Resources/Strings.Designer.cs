﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace CollaborationBot.Resources {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "16.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class Strings {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Strings() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("CollaborationBot.Resources.Strings", typeof(Strings).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Could not add {0} to project &apos;{1}&apos;..
        /// </summary>
        public static string AddMemberFailMessage {
            get {
                return ResourceManager.GetString("AddMemberFailMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Added {0} to project &apos;{1}&apos;..
        /// </summary>
        public static string AddMemberSuccessMessage {
            get {
                return ResourceManager.GetString("AddMemberSuccessMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to You are already a member of this project..
        /// </summary>
        public static string AlreadyJoinedMessage {
            get {
                return ResourceManager.GetString("AlreadyJoinedMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Something went wrong while processing the request on our backend..
        /// </summary>
        public static string BackendErrorMessage {
            get {
                return ResourceManager.GetString("BackendErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Server is already registered..
        /// </summary>
        public static string GuildExistsMessage {
            get {
                return ResourceManager.GetString("GuildExistsMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Your server is not registered! You can add it via command &apos;!!guild add&apos;..
        /// </summary>
        public static string GuildNotExistsMessage {
            get {
                return ResourceManager.GetString("GuildNotExistsMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to This user is already a member of the project..
        /// </summary>
        public static string MemberExistsMessage {
            get {
                return ResourceManager.GetString("MemberExistsMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to This user is not a member of the project..
        /// </summary>
        public static string MemberNotExistsMessage {
            get {
                return ResourceManager.GetString("MemberNotExistsMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to You are not a member of this project..
        /// </summary>
        public static string NotJoinedMessage {
            get {
                return ResourceManager.GetString("NotJoinedMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to A project owner can&apos;t leave their own project. Transfer ownership with `set-owner` first..
        /// </summary>
        public static string OwnerCannotLeaveMessage {
            get {
                return ResourceManager.GetString("OwnerCannotLeaveMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to This project already exists..
        /// </summary>
        public static string ProjectExistsMessage {
            get {
                return ResourceManager.GetString("ProjectExistsMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to This project does not exist..
        /// </summary>
        public static string ProjectNotExistMessage {
            get {
                return ResourceManager.GetString("ProjectNotExistMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Could not remove {0} from project &apos;{1}&apos;..
        /// </summary>
        public static string RemoveMemberFailMessage {
            get {
                return ResourceManager.GetString("RemoveMemberFailMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Removed {0} from project &apos;{1}&apos;..
        /// </summary>
        public static string RemoveMemberSuccessMessage {
            get {
                return ResourceManager.GetString("RemoveMemberSuccessMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Could not transfer ownership of project &apos;{0}&apos; to {1}..
        /// </summary>
        public static string SetOwnerFailMessage {
            get {
                return ResourceManager.GetString("SetOwnerFailMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Transfered ownership of project &apos;{0}&apos; to {1}..
        /// </summary>
        public static string SetOwnerSuccessMessage {
            get {
                return ResourceManager.GetString("SetOwnerSuccessMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Something went wrong when submitting part for project &apos;{0}&apos;..
        /// </summary>
        public static string SubmitPartFailMessage {
            get {
                return ResourceManager.GetString("SubmitPartFailMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Successfully submitted part for project &apos;{0}&apos;..
        /// </summary>
        public static string SubmitPartSuccessMessage {
            get {
                return ResourceManager.GetString("SubmitPartSuccessMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to test.
        /// </summary>
        public static string TestString {
            get {
                return ResourceManager.GetString("TestString", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to This user is already owner of project &apos;{0}&apos;..
        /// </summary>
        public static string UserAlreadyOwnerMessage {
            get {
                return ResourceManager.GetString("UserAlreadyOwnerMessage", resourceCulture);
            }
        }
    }
}
