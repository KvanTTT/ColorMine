﻿using System;
using System.Text;
using System.Runtime.InteropServices;

namespace ColorMine.ColorSpaces.Conversions
{
    // Code lovingly copied from StackOverflow (and tweaked a bit)
    // Question/Answer: http://stackoverflow.com/questions/5237104/c-sharp-convert-rgb-value-to-cmyk-using-an-icc-profile/5251318#5251318
    // Submitter: Codo http://stackoverflow.com/users/413337/codo
    // License: http://creativecommons.org/licenses/by-sa/3.0/
    public class WindowsColorSystem
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class ProfileFilename
        {
            public uint type;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string profileData;
            public uint dataSize;

            public ProfileFilename(string filename)
            {
                type = ProfileFilenameType;
                profileData = filename;
                dataSize = (uint)filename.Length * 2 + 2;
            }
        };

        public const uint ProfileFilenameType = 1;
        public const uint ProfileMembufferType = 2;

        public const uint ProfileRead = 1;
        public const uint ProfileReadWrite = 2;


        public enum FileShare : uint
        {
            Read = 1,
            Write = 2,
            Delete = 4
        };

        public enum CreateDisposition : uint
        {
            CreateNew = 1,
            CreateAlways = 2,
            OpenExisting = 3,
            OpenAlways = 4,
            TruncateExisting = 5
        };

        public enum LogicalColorSpace : uint
        {
            CalibratedRGB = 0x00000000,
            sRGB = 0x73524742,
            WindowsColorSpace = 0x57696E20
        };

        public enum ColorTransformMode : uint
        {
            ProofMode = 0x00000001,
            NormalMode = 0x00000002,
            BestMode = 0x00000003,
            EnableGamutChecking = 0x00010000,
            UseRelativeColorimetric = 0x00020000,
            FastTranslate = 0x00040000,
            PreserveBlack = 0x00100000,
            WCSAlways = 0x00200000
        };


        enum ColorType : int
        {
            Gray = 1,
            RGB = 2,
            XYZ = 3,
            Yxy = 4,
            Lab = 5,
            _3_Channel = 6,
            CMYK = 7,
            _5_Channel = 8,
            _6_Channel = 9,
            _7_Channel = 10,
            _8_Channel = 11,
            Named = 12
        };


        public const uint IntentPerceptual = 0;
        public const uint IntentRelativeColorimetric = 1;
        public const uint IntentSaturation = 2;
        public const uint IntentAbsoluteColorimetric = 3;

        public const uint IndexDontCare = 0;


        [StructLayout(LayoutKind.Sequential)]
        public struct RGBColor
        {
            public ushort red;
            public ushort green;
            public ushort blue;
            public ushort pad;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct CMYKColor
        {
            public ushort cyan;
            public ushort magenta;
            public ushort yellow;
            public ushort black;
        };

        [DllImport("mscms.dll", SetLastError = true, EntryPoint = "OpenColorProfileW", CallingConvention = CallingConvention.Winapi)]
        static extern IntPtr OpenColorProfile(
            [MarshalAs(UnmanagedType.LPStruct)] ProfileFilename profile,
            uint desiredAccess,
            FileShare shareMode,
            CreateDisposition creationMode);

        [DllImport("mscms.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        static extern bool CloseColorProfile(IntPtr hProfile);

        [DllImport("mscms.dll", SetLastError = true, EntryPoint = "GetStandardColorSpaceProfileW", CallingConvention = CallingConvention.Winapi)]
        static extern bool GetStandardColorSpaceProfile(
            uint machineName,
            LogicalColorSpace profileID,
            [MarshalAs(UnmanagedType.LPTStr), In, Out] StringBuilder profileName,
            ref uint size);

        [DllImport("mscms.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        static extern IntPtr CreateMultiProfileTransform(
            [In] IntPtr[] profiles,
            uint nProfiles,
            [In] uint[] intents,
            uint nIntents,
            ColorTransformMode flags,
            uint indexPreferredCMM);

        [DllImport("mscms.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        static extern bool DeleteColorTransform(IntPtr hTransform);

        [DllImport("mscms.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        static extern bool TranslateColors(
            IntPtr hColorTransform,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), In] RGBColor[] inputColors,
            uint nColors,
            ColorType ctInput,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), Out] CMYKColor[] outputColors,
            ColorType ctOutput);

        public static ICmyk TranslateColor(IRgb rgb, Uri cmykProfile, Uri rgbProfile)
        {
            const double dividend = 65535;
            var profileName = new StringBuilder(256);
            var size = (uint)profileName.Capacity * 2;

            ProfileFilename sRGBFilename;
            if (rgbProfile == null)
            {
                GetStandardColorSpaceProfile(0, LogicalColorSpace.sRGB, profileName, ref size);
                sRGBFilename = new ProfileFilename(profileName.ToString());
            }
            else
            {
                sRGBFilename = new ProfileFilename(rgbProfile.LocalPath);
            }

            var hSRGBProfile = OpenColorProfile(sRGBFilename, ProfileRead, FileShare.Read, CreateDisposition.OpenExisting);
            var isoCoatedFilename = new ProfileFilename(cmykProfile.LocalPath);
            var hIsoCoatedProfile = OpenColorProfile(isoCoatedFilename, ProfileRead, FileShare.Read, CreateDisposition.OpenExisting);

            var profiles = new[] { hSRGBProfile, hIsoCoatedProfile };
            var intents = new[] { IntentPerceptual };
            var transform = CreateMultiProfileTransform(profiles, 2, intents, 1, ColorTransformMode.BestMode, IndexDontCare);

            var rgbColors = new RGBColor[1];
            rgbColors[0] = new RGBColor();

            var cmykColors = new CMYKColor[1];
            cmykColors[0] = new CMYKColor();

            rgbColors[0].red = (ushort)(rgb.R * dividend / 255.0);
            rgbColors[0].green = (ushort)(rgb.G * dividend / 255.0);
            rgbColors[0].blue = (ushort)(rgb.B * dividend / 255.0);

            TranslateColors(transform, rgbColors, 1, ColorType.RGB, cmykColors, ColorType.CMYK);

            DeleteColorTransform(transform);
            CloseColorProfile(hSRGBProfile);
            CloseColorProfile(hIsoCoatedProfile);

            return new Cmyk
            {
                C = cmykColors[0].cyan / dividend,
                M = cmykColors[0].magenta / dividend,
                Y = cmykColors[0].yellow / dividend,
                K = cmykColors[0].black / dividend
            };
        }

        public static ICmyk TranslateColor(IRgb rgb, Uri cmykProfile)
        {
            var profileName = new StringBuilder(256);
            var size = (uint)profileName.Capacity * 2;
            GetStandardColorSpaceProfile(0, LogicalColorSpace.sRGB, profileName, ref size);
            var rgbFilename = new ProfileFilename(profileName.ToString());
            return TranslateColor(rgb, cmykProfile, new Uri(rgbFilename.profileData));
        }
    }
}