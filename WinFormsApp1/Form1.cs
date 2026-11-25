using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace WinFormsApp1
{
    public partial class Form1 : Form
    {
        // HIER DEINE APPLICATION ID AUS DEM DISCORD DEVELOPER PORTAL EINTRAGEN:
        private const string DiscordClientId = "1300000000000000000";

        private enum GameState
        {
            Loading,
            Auth,
            MainMenu,
            Tutorial,
            Playing,
            Paused,
            GameOver
        }

        private enum AuthMode
        {
            Login,
            Register
        }

        private enum MultiplayerView
        {
            None,
            ModeSelect,
            JoinByCode,
            LobbyRoom
        }

        private sealed class LobbyPlayer
        {
            public string Name { get; set; } = "";
            public int SkinIndex { get; set; } = 0;
            public bool IsHost { get; set; }
            public bool IsReady { get; set; } = true;
            public bool IsLocal { get; set; }
        }

        private sealed class CoOpMate
        {
            public string Name { get; set; } = "";
            public int SkinIndex { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Angle { get; set; }
            public int Hp { get; set; } = 100;
            public int MaxHp { get; set; } = 100;
            public int ShootCd { get; set; } = 0;
            public float WalkPhase { get; set; } = 0f;
            public bool IsMoving { get; set; } = false;
        }

        private enum MainTab
        {
            Haupt,
            Skin,
            Waffenkammer,
            Karriere,
            Shop,
            Spind,
            Spin,
            BattlePass,
            Einstellung
        }

        private enum SpindLockerTab
        {
            Waffen,
            Skins,
            Lobbymusik,
            Extras
        }

        private enum ShopSubTab
        {
            Upgrades,
            Skins,
            Waffen,
            Ausruestung
        }

        private enum SettingsCategory
        {
            Grafik,
            Audio,
            Gameplay
        }

        private enum CrosshairStyle
        {
            Cross,
            Dot,
            CircleDot,
            XScope
        }

        private enum SpinRewardKind
        {
            WeaponUnlockOrLevel,
            SkinUnlock,
            LobbyMusicUnlock
        }

        private sealed class SpinPrize
        {
            public string LabelDe { get; set; } = "";
            public string LabelEng { get; set; } = "";
            public SpinRewardKind Kind { get; set; }
            public int Index { get; set; }
            public Color Color { get; set; }
        }

        private sealed class BattlePassReward
        {
            public int Tier { get; set; }
            public string TitleDe { get; set; } = "";
            public string TitleEng { get; set; } = "";
            public int RewardCoins { get; set; }
            public int? UnlockSkin { get; set; }
            public int? UnlockWeapon { get; set; }
            public int? UnlockMusic { get; set; }
        }

        private enum MapType
        {
            RasenFluss,
            WaldBach,
            SumpfKanal,
            SteppeFluss
        }

        private enum PropKind
        {
            Tree,
            Bush,
            Reed,
            Rock
        }

        private enum PickupKind
        {
            Coin,
            Medkit
        }

        private sealed class UserAccount
        {
            public string Username { get; set; } = "";
            public string PasswordHash { get; set; } = "";
            public bool TutorialCompleted { get; set; } = false;

            public bool VSyncEnabled { get; set; } = true;
            public bool FullscreenF11 { get; set; } = true;
            public string Language { get; set; } = "DE";
            public float MasterVolume { get; set; } = 0.85f;
            public bool BloodEffects { get; set; } = true;
            public bool ScreenShakeEnabled { get; set; } = true;
            public bool HighParticles { get; set; } = true;
            public bool ShowCrosshair { get; set; } = true;
            public int CrosshairStyleIndex { get; set; } = 0;
            public int CrosshairColorIndex { get; set; } = 1;
            public int CrosshairSizeLevel { get; set; } = 2;
            public bool LobbyMusicEnabled { get; set; } = true;
            public int SelectedLobbyTrack { get; set; } = 0;
            public bool AutoReload { get; set; } = true;

            public int Coins { get; set; } = 450;
            public int TotalKills { get; set; } = 0;
            public int TotalScore { get; set; } = 0;
            public int BestWave { get; set; } = 1;
            public int GamesPlayed { get; set; } = 0;

            public int BattlePassXp { get; set; } = 0;
            public bool[] ClaimedPassTiers { get; set; } = new bool[8];

            public int FireRateLevel { get; set; } = 1;
            public int SpeedLevel { get; set; } = 1;
            public int MaxHpLevel { get; set; } = 1;
            public int StaminaRegenLevel { get; set; } = 1;
            public int CoinBoostLevel { get; set; } = 1;
            public int BonusArmorLevel { get; set; } = 0;

            public bool[] OwnedSkins { get; set; } = { true, false, false, false, false };
            public int EquippedSkin { get; set; } = 0;

            public bool[] OwnedWeapons { get; set; } = { true, false, false, false };
            public int[] WeaponLevels { get; set; } = { 1, 1, 1, 1 };
            public int[] WeaponMagBonus { get; set; } = { 0, 0, 0, 0 };
            public int EquippedWeapon { get; set; } = 0;

            public bool[] OwnedGear { get; set; } = { true, false, false, false };
            public int EquippedGear { get; set; } = 0;

            public bool[] OwnedLobbyMusic { get; set; } = { true, false, false, false };
        }

        private sealed class AccountsDatabase
        {
            public string? LastLoggedInUser { get; set; }
            public List<UserAccount> Accounts { get; set; } = new();
        }

        private sealed class NativeDiscordRpc : IDisposable
        {
            private readonly string clientId;
            private readonly object pipeLock = new();
            private NamedPipeClientStream? pipe;
            private bool connected;
            private bool disposed;
            private string lastPayloadSignature = "";
            private long runStartUnix = 0;

            public NativeDiscordRpc(string clientId)
            {
                this.clientId = clientId;
            }

            public void UpdatePresence(string details, string stateText, bool showTimer, string smallImageKey = "", string smallImageText = "")
            {
                if (disposed || string.IsNullOrWhiteSpace(clientId) || clientId == "1300000000000000000")
                    return;

                if (showTimer)
                {
                    if (runStartUnix == 0)
                        runStartUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                else
                {
                    runStartUnix = 0;
                }

                string sig = $"{details}|{stateText}|{runStartUnix}|{smallImageKey}";
                if (sig == lastPayloadSignature && connected)
                    return;

                lastPayloadSignature = sig;

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (pipeLock)
                    {
                        if (disposed) return;
                        try
                        {
                            if (!connected || pipe == null || !pipe.IsConnected)
                                ConnectInternal();

                            if (!connected || pipe == null || !pipe.IsConnected)
                                return;

                            var activity = new Dictionary<string, object>
                            {
                                ["details"] = details,
                                ["state"] = stateText,
                                ["assets"] = new Dictionary<string, string>
                                {
                                    ["large_image"] = "logo",
                                    ["large_text"] = "FLOS GAME - Realistic Survival",
                                    ["small_image"] = smallImageKey,
                                    ["small_text"] = smallImageText
                                }
                            };

                            if (runStartUnix > 0)
                            {
                                activity["timestamps"] = new Dictionary<string, long>
                                {
                                    ["start"] = runStartUnix
                                };
                            }

                            var frameObj = new Dictionary<string, object>
                            {
                                ["cmd"] = "SET_ACTIVITY",
                                ["args"] = new Dictionary<string, object>
                                {
                                    ["pid"] = Environment.ProcessId,
                                    ["activity"] = activity
                                },
                                ["nonce"] = Guid.NewGuid().ToString()
                            };

                            string json = JsonSerializer.Serialize(frameObj);
                            WriteFrame(pipe, 1, json);
                            DrainAvailable(pipe);
                        }
                        catch
                        {
                            DisconnectInternal();
                        }
                    }
                });
            }

            private void ConnectInternal()
            {
                DisconnectInternal();
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        var candidate = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.None);
                        candidate.Connect(120);
                        string handshake = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["v"] = 1,
                            ["client_id"] = clientId
                        });
                        WriteFrame(candidate, 0, handshake);
                        DrainAvailable(candidate);
                        pipe = candidate;
                        connected = true;
                        return;
                    }
                    catch { }
                }
            }

            private static void WriteFrame(Stream stream, int opcode, string json)
            {
                byte[] payload = Encoding.UTF8.GetBytes(json);
                byte[] header = new byte[8];
                BitConverter.GetBytes(opcode).CopyTo(header, 0);
                BitConverter.GetBytes(payload.Length).CopyTo(header, 4);
                stream.Write(header, 0, header.Length);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }

            private static void DrainAvailable(NamedPipeClientStream stream)
            {
                try
                {
                    if (!stream.CanRead) return;
                    Thread.Sleep(25);
                }
                catch { }
            }

            private void DisconnectInternal()
            {
                connected = false;
                try { pipe?.Dispose(); } catch { }
                pipe = null;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                lock (pipeLock)
                {
                    DisconnectInternal();
                }
            }
        }

        private sealed class Bullet
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public int Life = 80;
            public int Damage = 1;
            public Color Color = Color.Gold;
        }

        private sealed class ShellCasing
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public float Angle;
            public float Spin;
            public int Life;
        }

        private sealed class MuzzleFlash
        {
            public float X;
            public float Y;
            public float Angle;
            public int Life;
            public int MaxLife;
            public float Radius;
        }

        private sealed class SparkParticle
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public int Life;
            public int Size;
            public Color Color;
        }

        private sealed class PickupItem
        {
            public PickupKind Kind;
            public float X;
            public float Y;
            public int Value;
            public int Life;
            public float BobPhase;
        }

        private sealed class Zombie
        {
            public float X;
            public float Y;
            public float Speed;
            public int Hp;
            public int MaxHp;
            public int Size = 42;
            public float Angle;
            public float WalkPhase;
            public int HitFlashTicks = 0;
        }

        private sealed class BloodParticle
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public int Life;
            public int Size;
            public Color Color;
        }

        private sealed class BloodSplatter
        {
            public float X;
            public float Y;
            public int Size;
            public Color Color;
        }

        private sealed class TerrainPixel
        {
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public int Life;
            public int Size;
            public Color Color;
        }

        private sealed class GrassTuft
        {
            public int X;
            public int Y;
            public int W;
            public int H;
            public Color Color;
            public float SwayPhase;
        }

        private sealed class MapProp
        {
            public PropKind Kind;
            public float X;
            public float Y;
            public float Radius;
            public Color PrimaryColor;
            public Color SecondaryColor;
            public float SwayPhase;
        }

        private sealed class Star
        {
            public int X;
            public int Y;
            public int Size;
            public float Phase;
            public float Speed;
        }

        private sealed class CharacterRig
        {
            public Bitmap? FullPreview;
            public Bitmap? BodyAndHead;
            public Bitmap? LeftLeg;
            public Bitmap? RightLeg;
            public Bitmap? LeftArm;
            public Bitmap? RightArm;

            public void Dispose()
            {
                FullPreview?.Dispose();
                BodyAndHead?.Dispose();
                LeftLeg?.Dispose();
                RightLeg?.Dispose();
                LeftArm?.Dispose();
                RightArm?.Dispose();
            }
        }

        private sealed class PcmClip
        {
            public readonly float[] Samples;
            public readonly int SampleRate;
            public readonly bool IsFromFile;

            public PcmClip(float[] samples, int sampleRate, bool isFromFile)
            {
                Samples = samples;
                SampleRate = Math.Max(8000, sampleRate);
                IsFromFile = isFromFile;
            }
        }

        private sealed class ActiveVoice
        {
            public required float[] Samples;
            public double SourcePos;
            public double StepRatio;
            public float Gain;
            public bool Loop;
            public bool StopRequested;
            public bool IsLobbyMusic;
        }

        private sealed class WaveOutMixer : IDisposable
        {
            private const int SampleRate = 22050;
            private const int BufferSamples = 1102;
            private const int BufferCount = 4;
            private const uint CALLBACK_NULL = 0;
            private const uint WAVE_FORMAT_PCM = 1;
            private const uint WHDR_DONE = 0x00000001;
            private const uint WHDR_PREPARED = 0x00000002;
            private const int MMSYSERR_NOERROR = 0;

            [StructLayout(LayoutKind.Sequential)]
            private struct WAVEFORMATEX
            {
                public ushort wFormatTag;
                public ushort nChannels;
                public uint nSamplesPerSec;
                public uint nAvgBytesPerSec;
                public ushort nBlockAlign;
                public ushort wBitsPerSample;
                public ushort cbSize;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WAVEHDR
            {
                public IntPtr lpData;
                public uint dwBufferLength;
                public uint dwBytesRecorded;
                public IntPtr dwUser;
                public uint dwFlags;
                public uint dwLoops;
                public IntPtr lpNext;
                public IntPtr reserved;
            }

            private readonly object syncLock = new();
            private readonly List<ActiveVoice> voices = new();
            private readonly System.Windows.Forms.Timer mixTimer = new();
            private readonly IntPtr[] dataPtrs = new IntPtr[BufferCount];
            private readonly IntPtr[] hdrPtrs = new IntPtr[BufferCount];
            private readonly bool[] bufferQueued = new bool[BufferCount];
            private readonly int hdrSize;
            private IntPtr hWaveOut = IntPtr.Zero;
            private bool disposed;

            private ActiveVoice? movementVoice;
            private ActiveVoice? lobbyMusicVoice;

            public float MasterGain { get; set; } = 0.85f;
            public int OpenStatusCode { get; }

            public WaveOutMixer()
            {
                hdrSize = Marshal.SizeOf<WAVEHDR>();

                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = (ushort)WAVE_FORMAT_PCM,
                    nChannels = 1,
                    nSamplesPerSec = SampleRate,
                    nAvgBytesPerSec = SampleRate * 2,
                    nBlockAlign = 2,
                    wBitsPerSample = 16,
                    cbSize = 0
                };

                OpenStatusCode = waveOutOpen(out hWaveOut, unchecked((uint)-1), ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                if (OpenStatusCode != MMSYSERR_NOERROR || hWaveOut == IntPtr.Zero)
                {
                    hWaveOut = IntPtr.Zero;
                    return;
                }

                uint byteLen = BufferSamples * 2;
                for (int i = 0; i < BufferCount; i++)
                {
                    dataPtrs[i] = Marshal.AllocHGlobal((int)byteLen);
                    hdrPtrs[i] = Marshal.AllocHGlobal(hdrSize);

                    var hdr = new WAVEHDR
                    {
                        lpData = dataPtrs[i],
                        dwBufferLength = byteLen,
                        dwFlags = 0,
                        dwLoops = 0
                    };
                    Marshal.StructureToPtr(hdr, hdrPtrs[i], false);
                    waveOutPrepareHeader(hWaveOut, hdrPtrs[i], (uint)hdrSize);
                }

                mixTimer.Interval = 20;
                mixTimer.Tick += (_, _) => PumpBuffers();
                mixTimer.Start();
                PumpBuffers();
            }

            public void PlayOneShot(PcmClip? clip, float gain = 0.85f)
            {
                if (clip == null || clip.Samples.Length == 0 || MasterGain <= 0.001f) return;

                lock (syncLock)
                {
                    voices.Add(new ActiveVoice
                    {
                        Samples = clip.Samples,
                        SourcePos = 0,
                        StepRatio = clip.SampleRate / (double)SampleRate,
                        Gain = gain * MasterGain,
                        Loop = false,
                        IsLobbyMusic = false
                    });
                }
            }

            public void SetMovementLoop(PcmClip? clip, float gain = 0.65f, float speedFactor = 1.0f)
            {
                if (clip == null || clip.Samples.Length == 0 || MasterGain <= 0.001f)
                {
                    StopMovementLoop();
                    return;
                }

                double targetStep = (clip.SampleRate / (double)SampleRate) * Math.Clamp(speedFactor, 0.5f, 2.0f);
                float effectiveGain = gain * MasterGain;

                lock (syncLock)
                {
                    if (movementVoice != null && ReferenceEquals(movementVoice.Samples, clip.Samples) && !movementVoice.StopRequested)
                    {
                        movementVoice.Gain = effectiveGain;
                        movementVoice.StepRatio = targetStep;
                        return;
                    }

                    if (movementVoice != null)
                        movementVoice.StopRequested = true;

                    movementVoice = new ActiveVoice
                    {
                        Samples = clip.Samples,
                        SourcePos = 0,
                        StepRatio = targetStep,
                        Gain = effectiveGain,
                        Loop = true,
                        IsLobbyMusic = false
                    };
                    voices.Add(movementVoice);
                }
            }

            public void StopMovementLoop()
            {
                lock (syncLock)
                {
                    if (movementVoice != null)
                    {
                        movementVoice.StopRequested = true;
                        movementVoice = null;
                    }
                }
            }

            public void SetLobbyMusicLoop(PcmClip? clip, bool enabled, float gain = 0.24f)
            {
                if (!enabled || clip == null || clip.Samples.Length == 0 || MasterGain <= 0.001f)
                {
                    StopLobbyMusicLoop();
                    return;
                }

                float effectiveGain = gain * MasterGain;
                lock (syncLock)
                {
                    if (lobbyMusicVoice != null && ReferenceEquals(lobbyMusicVoice.Samples, clip.Samples) && !lobbyMusicVoice.StopRequested)
                    {
                        lobbyMusicVoice.Gain = effectiveGain;
                        return;
                    }

                    if (lobbyMusicVoice != null)
                        lobbyMusicVoice.StopRequested = true;

                    lobbyMusicVoice = new ActiveVoice
                    {
                        Samples = clip.Samples,
                        SourcePos = 0,
                        StepRatio = clip.SampleRate / (double)SampleRate,
                        Gain = effectiveGain,
                        Loop = true,
                        IsLobbyMusic = true
                    };
                    voices.Add(lobbyMusicVoice);
                }
            }

            public void StopLobbyMusicLoop()
            {
                lock (syncLock)
                {
                    if (lobbyMusicVoice != null)
                    {
                        lobbyMusicVoice.StopRequested = true;
                        lobbyMusicVoice = null;
                    }
                }
            }

            private void PumpBuffers()
            {
                if (disposed || hWaveOut == IntPtr.Zero) return;

                for (int i = 0; i < BufferCount; i++)
                {
                    if (bufferQueued[i])
                    {
                        var currentHdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtrs[i]);
                        if ((currentHdr.dwFlags & WHDR_DONE) == 0)
                            continue;
                        bufferQueued[i] = false;
                    }

                    short[] pcm = RenderMixedChunk(BufferSamples);
                    Marshal.Copy(pcm, 0, dataPtrs[i], pcm.Length);
                    bufferQueued[i] = true;
                    waveOutWrite(hWaveOut, hdrPtrs[i], (uint)hdrSize);
                }
            }

            private short[] RenderMixedChunk(int samplesCount)
            {
                float[] mix = new float[samplesCount];

                lock (syncLock)
                {
                    for (int v = voices.Count - 1; v >= 0; v--)
                    {
                        ActiveVoice voice = voices[v];
                        if (voice.StopRequested)
                        {
                            voices.RemoveAt(v);
                            continue;
                        }

                        float[] src = voice.Samples;
                        int len = src.Length;
                        if (len == 0)
                        {
                            voices.RemoveAt(v);
                            continue;
                        }

                        bool finished = false;
                        for (int i = 0; i < samplesCount; i++)
                        {
                            int idx = (int)voice.SourcePos;
                            if (idx >= len)
                            {
                                if (voice.Loop)
                                {
                                    voice.SourcePos %= len;
                                    idx = (int)voice.SourcePos;
                                }
                                else
                                {
                                    finished = true;
                                    break;
                                }
                            }

                            mix[i] += src[idx] * voice.Gain;
                            voice.SourcePos += voice.StepRatio;
                        }

                        if (finished)
                            voices.RemoveAt(v);
                    }
                }

                short[] output = new short[samplesCount];
                for (int i = 0; i < samplesCount; i++)
                {
                    float s = mix[i];
                    if (s > 1f) s = 1f - MathF.Exp(-(s - 1f));
                    else if (s < -1f) s = -1f + MathF.Exp(-(-s - 1f));
                    output[i] = (short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue);
                }

                return output;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                mixTimer.Stop();
                mixTimer.Dispose();

                if (hWaveOut != IntPtr.Zero)
                {
                    waveOutReset(hWaveOut);
                    for (int i = 0; i < BufferCount; i++)
                    {
                        if (hdrPtrs[i] != IntPtr.Zero)
                        {
                            var hdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtrs[i]);
                            if ((hdr.dwFlags & WHDR_PREPARED) != 0)
                                waveOutUnprepareHeader(hWaveOut, hdrPtrs[i], (uint)hdrSize);
                            Marshal.FreeHGlobal(hdrPtrs[i]);
                            hdrPtrs[i] = IntPtr.Zero;
                        }
                        if (dataPtrs[i] != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(dataPtrs[i]);
                            dataPtrs[i] = IntPtr.Zero;
                        }
                    }
                    waveOutClose(hWaveOut);
                    hWaveOut = IntPtr.Zero;
                }
            }

            [DllImport("winmm.dll")]
            private static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx,
                IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

            [DllImport("winmm.dll")]
            private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr lpWaveOutHdr, uint uSize);

            [DllImport("winmm.dll")]
            private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr lpWaveOutHdr, uint uSize);

            [DllImport("winmm.dll")]
            private static extern int waveOutWrite(IntPtr hwo, IntPtr lpWaveOutHdr, uint uSize);

            [DllImport("winmm.dll")]
            private static extern int waveOutReset(IntPtr hwo);

            [DllImport("winmm.dll")]
            private static extern int waveOutClose(IntPtr hwo);
        }

        private GameState state = GameState.Loading;
        private AuthMode authMode = AuthMode.Register;
        private MainTab activeTab = MainTab.Haupt;
        private SpindLockerTab activeSpindTab = SpindLockerTab.Waffen;
        private ShopSubTab activeShopTab = ShopSubTab.Upgrades;
        private SettingsCategory activeSettingsCat = SettingsCategory.Grafik;

        private MultiplayerView mpView = MultiplayerView.None;
        private string currentRoomCode = "";
        private string joinCodeInput = "";
        private string mpStatusMessage = "";
        private bool mpStatusIsError = false;
        private bool isMultiplayerMatchActive = false;
        private readonly List<LobbyPlayer> lobbyPlayers = new();
        private readonly List<CoOpMate> activeCoOpMates = new();

        private Rectangle multiplayerBtnRect;
        private Rectangle mpCloseBtnRect;
        private Rectangle mpCreateLobbyBtnRect;
        private Rectangle mpJoinByCodeBtnRect;
        private Rectangle mpCodeInputRect;
        private Rectangle mpConfirmJoinBtnRect;
        private Rectangle mpBackToSelectBtnRect;
        private Rectangle mpCopyCodeBtnRect;
        private Rectangle mpStartMatchBtnRect;
        private Rectangle mpLeaveLobbyBtnRect;

        private bool vsyncEnabled = true;
        private bool fullscreenF11 = true;
        private Rectangle windowedBounds = new Rectangle(120, 80, 1024, 640);
        private bool cursorHiddenByGame = false;

        private string language = "DE";
        private float masterVolume = 0.85f;
        private bool bloodEffects = true;
        private bool screenShakeEnabled = true;
        private bool highParticles = true;
        private bool showCrosshair = true;
        private int crosshairStyleIndex = 0;
        private int crosshairColorIndex = 1;
        private int crosshairSizeLevel = 2;
        private bool lobbyMusicEnabled = true;
        private int selectedLobbyTrack = 0;
        private bool autoReload = true;

        private readonly Color[] crosshairPalette =
        {
            Color.White,
            Color.Cyan,
            Color.LimeGreen,
            Color.OrangeRed,
            Color.Gold
        };
        private readonly string[] crosshairColorNamesDe = { "Weiß", "Cyan", "Grün", "Rot", "Gold" };
        private readonly string[] crosshairColorNamesEng = { "White", "Cyan", "Green", "Red", "Gold" };
        private readonly string[] crosshairStyleNamesDe = { "Kreuz", "Punkt", "Kreis+Punkt", "X-Scope" };
        private readonly string[] crosshairStyleNamesEng = { "Cross", "Dot", "Circle+Dot", "X-Scope" };

        private readonly Rectangle[] spindLockerTabRects = new Rectangle[4];
        private readonly Rectangle[] settingsCatRects = new Rectangle[3];
        private Rectangle setBtnA;
        private Rectangle setBtnB;
        private Rectangle setBtnC;
        private Rectangle setBtnD;
        private Rectangle setBtnE;
        private Rectangle setBtnF;

        private const int SpinCost = 120;
        private float spinAngle = 0f;
        private float spinVelocity = 0f;
        private bool isSpinning = false;
        private int selectedSpinPrizeIdx = 0;
        private string lastSpinResultText = "";
        private Rectangle spinBtnRect;
        private Rectangle spinWheelRect;

        private readonly string[] lobbyTrackNamesDe =
        {
            "Lobby Pulse #1",
            "Hardtekk Kick Loop",
            "Night Synth Drift",
            "Cyber Bass Wave"
        };
        private readonly string[] lobbyTrackNamesEng =
        {
            "Lobby Pulse #1",
            "Hardtekk Kick Loop",
            "Night Synth Drift",
            "Cyber Bass Wave"
        };
        private readonly int[] lobbyTrackPrices = { 0, 150, 220, 300 };
        private readonly bool[] ownedLobbyMusic = { true, false, false, false };
        private readonly PcmClip?[] lobbyMusicClips = new PcmClip?[4];

        private readonly List<SpinPrize> spinPrizes = new()
        {
            new SpinPrize { LabelDe = "MP5-SMG", LabelEng = "MP5 SMG", Kind = SpinRewardKind.WeaponUnlockOrLevel, Index = 1, Color = Color.FromArgb(45, 130, 230) },
            new SpinPrize { LabelDe = "Neon-Skin", LabelEng = "Neon Skin", Kind = SpinRewardKind.SkinUnlock, Index = 1, Color = Color.FromArgb(38, 165, 85) },
            new SpinPrize { LabelDe = "Hardtekk Loop", LabelEng = "Hardtekk Loop", Kind = SpinRewardKind.LobbyMusicUnlock, Index = 1, Color = Color.FromArgb(215, 95, 30) },
            new SpinPrize { LabelDe = "Schrot-Flinte", LabelEng = "Shotgun", Kind = SpinRewardKind.WeaponUnlockOrLevel, Index = 2, Color = Color.FromArgb(155, 65, 210) },
            new SpinPrize { LabelDe = "Feuer-Skin", LabelEng = "Fire Skin", Kind = SpinRewardKind.SkinUnlock, Index = 2, Color = Color.FromArgb(205, 50, 50) },
            new SpinPrize { LabelDe = "Night Synth", LabelEng = "Night Synth", Kind = SpinRewardKind.LobbyMusicUnlock, Index = 2, Color = Color.FromArgb(0, 150, 136) },
            new SpinPrize { LabelDe = "Sturm-Gewehr", LabelEng = "Assault Rifle", Kind = SpinRewardKind.WeaponUnlockOrLevel, Index = 3, Color = Color.FromArgb(225, 175, 25) },
            new SpinPrize { LabelDe = "Gold-Skin", LabelEng = "Gold Skin", Kind = SpinRewardKind.SkinUnlock, Index = 4, Color = Color.FromArgb(70, 130, 180) }
        };

        private const int XpPerTier = 250;
        private int battlePassXp = 0;
        private readonly bool[] claimedPassTiers = new bool[8];
        private readonly List<BattlePassReward> battlePassRewards = new()
        {
            new BattlePassReward { Tier = 1, TitleDe = "+120 Coins", TitleEng = "+120 Coins", RewardCoins = 120 },
            new BattlePassReward { Tier = 2, TitleDe = "Neon-Grün Skin", TitleEng = "Neon Green Skin", RewardCoins = 50, UnlockSkin = 1 },
            new BattlePassReward { Tier = 3, TitleDe = "Hardtekk Musik", TitleEng = "Hardtekk Music", RewardCoins = 75, UnlockMusic = 1 },
            new BattlePassReward { Tier = 4, TitleDe = "MP5-SMG Waffe", TitleEng = "MP5 SMG Weapon", RewardCoins = 100, UnlockWeapon = 1 },
            new BattlePassReward { Tier = 5, TitleDe = "Feuer-Rot Skin", TitleEng = "Fire Red Skin", RewardCoins = 120, UnlockSkin = 2 },
            new BattlePassReward { Tier = 6, TitleDe = "Night Synth Musik", TitleEng = "Night Synth Music", RewardCoins = 140, UnlockMusic = 2 },
            new BattlePassReward { Tier = 7, TitleDe = "Schrot-Flinte", TitleEng = "Shotgun Weapon", RewardCoins = 160, UnlockWeapon = 2 },
            new BattlePassReward { Tier = 8, TitleDe = "Gold-Skin + 250C", TitleEng = "Gold Skin + 250C", RewardCoins = 250, UnlockSkin = 4 }
        };

        private Rectangle pauseResumeRect;
        private Rectangle pauseMenuRect;

        private bool IsEnglish => string.Equals(language, "ENG", StringComparison.OrdinalIgnoreCase);
        private string T(string de, string eng) => IsEnglish ? eng : de;
        private string GetLobbyTrackName(int idx) => IsEnglish ? lobbyTrackNamesEng[idx] : lobbyTrackNamesDe[idx];

        private AccountsDatabase accountsDb = new();
        private UserAccount? currentAccount;
        private string authUsername = "";
        private string authPassword = "";
        private int authFocusField = 0;
        private string authMessage = "";
        private bool authMessageIsError = false;
        private Rectangle authModeLoginRect;
        private Rectangle authModeRegisterRect;
        private Rectangle authUserBoxRect;
        private Rectangle authPassBoxRect;
        private Rectangle authSubmitRect;
        private Rectangle logoutBtnRect;

        private NativeDiscordRpc? discordRpc;
        private int discordSyncTimer = 0;

        private WaveOutMixer? audioMixer;
        private PcmClip? shootClip;
        private PcmClip? swimClip;
        private PcmClip? grassRunClip;
        private PcmClip? hurtClip;
        private PcmClip? hitClip;
        private PcmClip? waveClip;
        private PcmClip? dashClip;
        private PcmClip? reloadClip;
        private PcmClip? pickupClip;
        private PcmClip? spinTickClip;

        private int hurtSoundCooldown = 0;
        private int terrainParticleTimer = 0;

        private float loadProgress = 0f;
        private float spinnerAngle = 0f;
        private float loadingFxPhase = 0f;
        private int tipIndex = 0;
        private int tipTicks = 0;
        private readonly List<Star> stars = new();

        private readonly string[] gtaTipsDe =
        {
            "Tipp: F11 schaltet wie im Browser zwischen Vollbild und Fenster um.",
            "Tipp: Im Kampf ist die Maus versteckt – dein Custom-Fadenkreuz führt das Ziel.",
            "Tipp: Über MULTIPLAYER erstellst du Lobbys mit Server-Code oder trittst bei.",
            "Tipp: Mit LEERTASTE dasht du, mit R lädst du nach."
        };
        private readonly string[] gtaTipsEng =
        {
            "Tip: Press F11 like in a browser to toggle fullscreen and window mode.",
            "Tip: Mouse is hidden in combat – aim with your custom crosshair.",
            "Tip: Use MULTIPLAYER to host lobbies with a server code or join by code.",
            "Tip: Press SPACE to dash, press R to reload."
        };

        private bool tutorialCompleted = false;
        private int tutorialStep = 0;
        private readonly string[] tutorialTitlesDe =
        {
            "SCHRITT 1/4: BEWEGUNG, SPRINT & DASH",
            "SCHRITT 2/4: SCHWIMMEN & TERRAIN",
            "SCHRITT 3/4: WAFFEN, MAGAZIN & SHOP",
            "SCHRITT 4/4: SPIND, SPIN & BATTLEPASS"
        };
        private readonly string[] tutorialTitlesEng =
        {
            "STEP 1/4: MOVEMENT, SPRINT & DASH",
            "STEP 2/4: SWIMMING & TERRAIN",
            "STEP 3/4: WEAPONS, MAGAZINES & SHOP",
            "STEP 4/4: SPIND, SPIN & BATTLEPASS"
        };
        private readonly string[] tutorialTextsDe =
        {
            "WASD/Pfeile = Gehen, SHIFT = Rennen, LEERTASTE = Dash mit kurzem Ausweich-Schutz.",
            "Auf Rasen fliegen Rasen-Pixel mit Schritt-Sounds. Im Fluss schwimmst du mit Wasser-Partikeln.",
            "Linksklick = Schießen, R = Nachladen. Im Shop levelst du Waffen, Magazin, Ausdauer & Coins.",
            "Im SPIND verwaltest du Waffen, Skins & Lobbymusik, drehst im SPIN und holst PASS-Stufen ab."
        };
        private readonly string[] tutorialTextsEng =
        {
            "WASD/Arrows = Walk, SHIFT = Sprint, SPACE = Dash with brief evasion protection.",
            "Grass kicks up grass pixels with footsteps. Rivers slow you down with water particles.",
            "Left-Click = Shoot, R = Reload. Upgrade weapons, magazine, stamina & coins in Shop.",
            "Manage weapons, skins & lobby music in SPIND, spin in SPIN, and claim PASS rewards."
        };
        private Rectangle tutNextRect;
        private Rectangle tutSkipRect;

        private string gamerTag = "Flo";
        private bool editingTag = false;
        private string tagWarning = "";

        private readonly string[] blacklistedWords =
        {
            "nazi", "hitler", "kkk", "nigger", "nigga", "faggot", "fag", "retard",
            "whore", "slut", "bitch", "bastard", "arschloch", "wichser", "hurensohn",
            "fotze", "spast", "kanake", "schwuchtel", "rape", "kill yourself", "kys"
        };

        private int coins = 450;
        private int totalKills = 0;
        private int totalScore = 0;
        private int bestWave = 1;
        private int gamesPlayed = 0;

        private int fireRateLevel = 1;
        private int speedLevel = 1;
        private int maxHpLevel = 1;
        private int staminaRegenLevel = 1;
        private int coinBoostLevel = 1;
        private int bonusArmorLevel = 0;

        private readonly string[] skinNamesDe = { "Blau-Jacke", "Neon-Grün", "Feuer-Rot", "Nacht-Shadow", "Gold-Runner" };
        private readonly string[] skinNamesEng = { "Blue Jacket", "Neon Green", "Fire Red", "Night Shadow", "Gold Runner" };
        private readonly bool[] ownedSkins = { true, false, false, false, false };
        private readonly int[] skinPrices = { 0, 140, 220, 320, 480 };
        private int equippedSkin = 0;

        private readonly string[] weaponNamesDe = { "Rost-Pistole", "MP5-SMG", "Schrot-Flinte", "Sturm-Gewehr" };
        private readonly string[] weaponNamesEng = { "Rust Pistol", "MP5 SMG", "Shotgun", "Assault Rifle" };
        private readonly int[] weaponMagSizes = { 12, 28, 6, 24 };
        private readonly int[] weaponReloadTicks = { 38, 45, 55, 50 };
        private readonly bool[] ownedWeapons = { true, false, false, false };
        private readonly int[] weaponPrices = { 0, 180, 320, 480 };
        private readonly int[] weaponLevels = { 1, 1, 1, 1 };
        private readonly int[] weaponMagBonus = { 0, 0, 0, 0 };
        private int equippedWeapon = 0;

        private int currentAmmo = 12;
        private int reloadTimer = 0;
        private int reloadDuration = 38;

        private readonly string[] gearNamesDe = { "Stoff-Weste", "Kevlar-Weste", "Med-Pack+", "Exo-Panzer" };
        private readonly string[] gearNamesEng = { "Cloth Vest", "Kevlar Vest", "Med-Pack+", "Exo Armor" };
        private readonly bool[] ownedGear = { true, false, false, false };
        private readonly int[] gearPrices = { 0, 160, 260, 420 };
        private int equippedGear = 0;

        private string GetSkinName(int idx) => IsEnglish ? skinNamesEng[idx] : skinNamesDe[idx];
        private string GetWeaponName(int idx) => IsEnglish ? weaponNamesEng[idx] : weaponNamesDe[idx];
        private string GetGearName(int idx) => IsEnglish ? gearNamesEng[idx] : gearNamesDe[idx];

        private readonly Rectangle[] mainTabRects = new Rectangle[9];
        private readonly Rectangle[] shopSubTabRects = new Rectangle[4];
        private Rectangle tagBoxRect;
        private Rectangle startBtnRect;
        private readonly List<Rectangle> currentCardRects = new();

        private readonly CharacterRig?[] humanRigs = new CharacterRig?[5];
        private CharacterRig? zombieRig;

        private MapType currentMap = MapType.RasenFluss;
        private float riverCenterX = 512f;
        private float riverWidth = 110f;
        private bool horizontalRiver = false;
        private float riverCenterY = 320f;
        private float riverCurveSeed = 0f;
        private float waterWavePhase = 0f;
        private readonly List<GrassTuft> grassTufts = new();
        private readonly List<MapProp> mapProps = new();

        private float runElapsedSeconds = 0f;
        private int nextFiveMinuteIndex = 1;
        private float mapTransitionProgress = 0f;
        private bool mapTransitionSwapped = false;
        private string mapBannerText = "";
        private int mapBannerTicks = 0;

        private float damageFlashAlpha = 0f;
        private float gunRecoil = 0f;
        private float screenShakeMag = 0f;

        private float playerX;
        private float playerY;
        private float playerAngle = 0f;
        private const int PlayerSize = 44;
        private int playerHp = 100;
        private int maxPlayerHp = 100;
        private float stamina = 100f;
        private const float MaxStamina = 100f;
        private bool isExhausted = false;
        private bool isMovingNow = false;
        private bool isSprintingNow = false;
        private bool isSwimmingNow = false;
        private float walkAnimPhase = 0f;

        private int dashTimer = 0;
        private int dashCooldown = 0;
        private float dashDirX = 0f;
        private float dashDirY = 0f;

        private PointF mousePos;
        private bool mouseDown = false;
        private int shootCooldown = 0;

        private int wave = 1;
        private int zombiesToSpawnInWave = 6;
        private int spawnCooldown = 20;
        private int score = 0;
        private int kills = 0;

        private readonly List<Bullet> bullets = new();
        private readonly List<ShellCasing> shellCasings = new();
        private readonly List<MuzzleFlash> muzzleFlashes = new();
        private readonly List<SparkParticle> sparkParticles = new();
        private readonly List<PickupItem> pickups = new();
        private readonly List<Zombie> zombies = new();
        private readonly List<BloodParticle> bloodParticles = new();
        private readonly List<BloodSplatter> bloodSplatters = new();
        private readonly List<TerrainPixel> terrainPixels = new();
        private readonly Random rng = new();
        private readonly System.Windows.Forms.Timer loopTimer = new();
        private bool resourcesCleanedUp = false;

        private string AccountsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");

        public Form1()
        {
            InitializeComponent();

            this.Text = "FLOS GAME - Realistic Pixel Zombie Survival";
            this.Size = windowedBounds.Size;
            this.Location = windowedBounds.Location;
            this.DoubleBuffered = true;
            this.KeyPreview = true;

            LoadAccountsDatabase();
            ApplyFullscreenMode(fullscreenF11);
            BuildSounds();
            ApplyAudioAndSync();
            discordRpc = new NativeDiscordRpc(DiscordClientId);
            SyncDiscordPresence(force: true);

            BuildStars();
            BuildCharacterRigs();
            GenerateMapEnvironment(MapType.RasenFluss);
            UpdateLayoutRects();
            ResetPlayerAndWorld();

            loopTimer.Tick += OnTick;
            loopTimer.Start();

            this.Resize += (_, _) => { UpdateLayoutRects(); GenerateMapEnvironment(currentMap); };
            this.Deactivate += (_, _) => { mouseDown = false; SetCombatCursorHidden(false); };
            this.Activated += (_, _) => { SyncCombatCursorVisibility(); };
            this.MouseMove += (_, e) => mousePos = e.Location;
            this.MouseDown += OnMouseDown;
            this.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) mouseDown = false; };
            this.KeyDown += OnKeyDown;
            this.KeyPress += OnKeyPress;
        }

        private string GenerateServerCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] code = new char[6];
            for (int i = 0; i < code.Length; i++)
                code[i] = chars[rng.Next(chars.Length)];
            return new string(code);
        }

        private void OpenMultiplayerMenu()
        {
            mpView = MultiplayerView.ModeSelect;
            mpStatusMessage = "";
            mpStatusIsError = false;
            this.Invalidate();
        }

        private void CreateMultiplayerLobby()
        {
            string tag = string.IsNullOrWhiteSpace(gamerTag) ? T("Spieler", "Player") : gamerTag;
            currentRoomCode = GenerateServerCode();
            lobbyPlayers.Clear();
            lobbyPlayers.Add(new LobbyPlayer
            {
                Name = tag,
                SkinIndex = equippedSkin,
                IsHost = true,
                IsReady = true,
                IsLocal = true
            });
            lobbyPlayers.Add(new LobbyPlayer
            {
                Name = "Alex_Squad",
                SkinIndex = (equippedSkin + 1) % skinNamesDe.Length,
                IsHost = false,
                IsReady = true,
                IsLocal = false
            });
            mpStatusMessage = T("Lobby erstellt! Code rechts teilen.", "Lobby created! Share code on the right.");
            mpStatusIsError = false;
            mpView = MultiplayerView.LobbyRoom;
            SyncDiscordPresence(force: true);
        }

        private void JoinMultiplayerLobbyWithCode()
        {
            string cleaned = joinCodeInput.Trim().ToUpperInvariant();
            if (cleaned.Length < 4)
            {
                mpStatusMessage = T("Bitte einen gültigen Server-Code (min. 4 Zeichen) eingeben!", "Please enter a valid server code (min. 4 chars)!");
                mpStatusIsError = true;
                return;
            }

            string tag = string.IsNullOrWhiteSpace(gamerTag) ? T("Spieler", "Player") : gamerTag;
            currentRoomCode = cleaned;
            lobbyPlayers.Clear();
            lobbyPlayers.Add(new LobbyPlayer
            {
                Name = "Host_" + cleaned[..Math.Min(3, cleaned.Length)],
                SkinIndex = 2,
                IsHost = true,
                IsReady = true,
                IsLocal = false
            });
            lobbyPlayers.Add(new LobbyPlayer
            {
                Name = tag,
                SkinIndex = equippedSkin,
                IsHost = false,
                IsReady = true,
                IsLocal = true
            });
            mpStatusMessage = T($"Verbunden mit Server {currentRoomCode}!", $"Connected to server {currentRoomCode}!");
            mpStatusIsError = false;
            mpView = MultiplayerView.LobbyRoom;
            SyncDiscordPresence(force: true);
        }

        private void StartMultiplayerMatchFromLobby()
        {
            if (currentAccount == null)
            {
                mpView = MultiplayerView.None;
                state = GameState.Auth;
                SyncCombatCursorVisibility();
                return;
            }

            isMultiplayerMatchActive = true;
            mpView = MultiplayerView.None;
            SaveGameData();
            ResetPlayerAndWorld();
            SpawnCoOpMatesFromLobby();
            mapBannerText = T($"MULTIPLAYER [{currentRoomCode}] • Welle 1", $"MULTIPLAYER [{currentRoomCode}] • Wave 1");
            mapBannerTicks = 130;
            state = GameState.Playing;
            SyncCombatCursorVisibility();
            SyncDiscordPresence(force: true);
        }

        private void SpawnCoOpMatesFromLobby()
        {
            activeCoOpMates.Clear();
            int idx = 1;
            foreach (LobbyPlayer lp in lobbyPlayers)
            {
                if (lp.IsLocal) continue;
                float angleOffset = idx * 1.4f;
                activeCoOpMates.Add(new CoOpMate
                {
                    Name = lp.Name,
                    SkinIndex = Math.Clamp(lp.SkinIndex, 0, humanRigs.Length - 1),
                    X = playerX + MathF.Cos(angleOffset) * 58f,
                    Y = playerY + MathF.Sin(angleOffset) * 58f,
                    Angle = 0f,
                    Hp = maxPlayerHp,
                    MaxHp = maxPlayerHp,
                    WalkPhase = idx * 1.1f
                });
                idx++;
            }
        }

        private void UpdateCoOpMates()
        {
            if (!isMultiplayerMatchActive || activeCoOpMates.Count == 0) return;

            int slot = 0;
            foreach (CoOpMate mate in activeCoOpMates)
            {
                slot++;
                float orbitAngle = runElapsedSeconds * 0.9f + slot * 2.1f;
                float targetX = Math.Clamp(playerX + MathF.Cos(orbitAngle) * 72f, 35f, this.ClientSize.Width - 35f);
                float targetY = Math.Clamp(playerY + MathF.Sin(orbitAngle) * 72f, 80f, this.ClientSize.Height - 35f);

                float mdx = targetX - mate.X;
                float mdy = targetY - mate.Y;
                float dist = MathF.Sqrt(mdx * mdx + mdy * mdy);

                if (dist > 6f)
                {
                    float spd = GetPlayerSpeed() * 0.88f;
                    mate.X += (mdx / dist) * spd;
                    mate.Y += (mdy / dist) * spd;
                    mate.WalkPhase += 0.28f;
                    mate.IsMoving = true;
                }
                else
                {
                    mate.IsMoving = false;
                }

                Zombie? nearest = null;
                float bestDistSq = float.MaxValue;
                foreach (Zombie z in zombies)
                {
                    float dSq = (z.X - mate.X) * (z.X - mate.X) + (z.Y - mate.Y) * (z.Y - mate.Y);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        nearest = z;
                    }
                }

                if (nearest != null)
                {
                    mate.Angle = MathF.Atan2(nearest.Y - mate.Y, nearest.X - mate.X);
                    if (mate.ShootCd > 0) mate.ShootCd--;
                    if (mate.ShootCd == 0 && bestDistSq < 520f * 520f)
                    {
                        float bx = mate.X + MathF.Cos(mate.Angle) * 20f;
                        float by = mate.Y + MathF.Sin(mate.Angle) * 20f;
                        bullets.Add(new Bullet
                        {
                            X = bx,
                            Y = by,
                            Vx = MathF.Cos(mate.Angle) * 14f,
                            Vy = MathF.Sin(mate.Angle) * 14f,
                            Damage = Math.Max(1, GetWeaponDamage(equippedWeapon) / 2),
                            Color = Color.LimeGreen
                        });
                        mate.ShootCd = 24;
                    }
                }
                else if (dist > 6f)
                {
                    mate.Angle = MathF.Atan2(mdy, mdx);
                }
            }
        }

        private void ApplyFullscreenMode(bool enableFullscreen)
        {
            fullscreenF11 = enableFullscreen;
            if (fullscreenF11)
            {
                if (this.WindowState == FormWindowState.Normal && this.FormBorderStyle != FormBorderStyle.None)
                    windowedBounds = new Rectangle(this.Location, this.Size);

                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
                Rectangle target = windowedBounds.Width >= 640 && windowedBounds.Height >= 400
                    ? windowedBounds
                    : new Rectangle(120, 80, 1024, 640);
                this.Bounds = target;
            }

            UpdateLayoutRects();
            GenerateMapEnvironment(currentMap);
        }

        private void ToggleFullscreenF11()
        {
            ApplyFullscreenMode(!fullscreenF11);
            SaveGameData();
        }

        private void SetCombatCursorHidden(bool hide)
        {
            if (hide == cursorHiddenByGame) return;
            try
            {
                if (hide)
                    Cursor.Hide();
                else
                    Cursor.Show();
                cursorHiddenByGame = hide;
            }
            catch { }
        }

        private void SyncCombatCursorVisibility()
        {
            bool shouldHide = (state == GameState.Playing) && this.ContainsFocus && !this.IsDisposed;
            SetCombatCursorHidden(shouldHide);
        }

        private void ApplyAudioAndSync()
        {
            loopTimer.Interval = vsyncEnabled ? 16 : 8;
            if (audioMixer != null)
            {
                audioMixer.MasterGain = Math.Clamp(masterVolume, 0f, 1f);
                UpdateLobbyMusicState();
            }
        }

        private void UpdateLobbyMusicState()
        {
            if (audioMixer == null) return;
            bool inLobby = (state == GameState.MainMenu || state == GameState.Auth);
            int idx = Math.Clamp(selectedLobbyTrack, 0, lobbyMusicClips.Length - 1);
            if (!ownedLobbyMusic[idx]) idx = 0;
            audioMixer.SetLobbyMusicLoop(lobbyMusicClips[idx], enabled: inLobby && lobbyMusicEnabled, gain: 0.22f);
        }

        private static string HashPassword(string password)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes);
        }

        private void LoadAccountsDatabase()
        {
            try
            {
                if (!File.Exists(AccountsFilePath))
                {
                    accountsDb = new AccountsDatabase();
                    authMode = AuthMode.Register;
                    return;
                }

                string json = File.ReadAllText(AccountsFilePath, Encoding.UTF8);
                AccountsDatabase? db = JsonSerializer.Deserialize<AccountsDatabase>(json);
                if (db != null)
                {
                    accountsDb = db;
                    authMode = accountsDb.Accounts.Count > 0 ? AuthMode.Login : AuthMode.Register;
                }
            }
            catch
            {
                accountsDb = new AccountsDatabase();
                authMode = AuthMode.Register;
            }
        }

        private void SaveAccountsDatabase()
        {
            try
            {
                if (currentAccount != null)
                {
                    ApplyCurrentStateToAccount(currentAccount);
                    accountsDb.LastLoggedInUser = currentAccount.Username;
                }

                string json = JsonSerializer.Serialize(accountsDb, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AccountsFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private void ApplyCurrentStateToAccount(UserAccount acc)
        {
            acc.TutorialCompleted = tutorialCompleted;
            acc.VSyncEnabled = vsyncEnabled;
            acc.FullscreenF11 = fullscreenF11;
            acc.Language = language;
            acc.MasterVolume = masterVolume;
            acc.BloodEffects = bloodEffects;
            acc.ScreenShakeEnabled = screenShakeEnabled;
            acc.HighParticles = highParticles;
            acc.ShowCrosshair = showCrosshair;
            acc.CrosshairStyleIndex = crosshairStyleIndex;
            acc.CrosshairColorIndex = crosshairColorIndex;
            acc.CrosshairSizeLevel = crosshairSizeLevel;
            acc.LobbyMusicEnabled = lobbyMusicEnabled;
            acc.SelectedLobbyTrack = selectedLobbyTrack;
            acc.AutoReload = autoReload;

            acc.Coins = coins;
            acc.TotalKills = totalKills;
            acc.TotalScore = totalScore;
            acc.BestWave = bestWave;
            acc.GamesPlayed = gamesPlayed;

            acc.BattlePassXp = battlePassXp;
            acc.ClaimedPassTiers = (bool[])claimedPassTiers.Clone();

            acc.FireRateLevel = fireRateLevel;
            acc.SpeedLevel = speedLevel;
            acc.MaxHpLevel = maxHpLevel;
            acc.StaminaRegenLevel = staminaRegenLevel;
            acc.CoinBoostLevel = coinBoostLevel;
            acc.BonusArmorLevel = bonusArmorLevel;

            acc.OwnedSkins = (bool[])ownedSkins.Clone();
            acc.EquippedSkin = equippedSkin;

            acc.OwnedWeapons = (bool[])ownedWeapons.Clone();
            acc.WeaponLevels = (int[])weaponLevels.Clone();
            acc.WeaponMagBonus = (int[])weaponMagBonus.Clone();
            acc.EquippedWeapon = equippedWeapon;

            acc.OwnedGear = (bool[])ownedGear.Clone();
            acc.EquippedGear = equippedGear;

            acc.OwnedLobbyMusic = (bool[])ownedLobbyMusic.Clone();
        }

        private void LoadAccountIntoGame(UserAccount acc)
        {
            currentAccount = acc;
            gamerTag = acc.Username;
            tutorialCompleted = acc.TutorialCompleted;

            vsyncEnabled = acc.VSyncEnabled;
            ApplyFullscreenMode(acc.FullscreenF11);
            language = string.Equals(acc.Language, "ENG", StringComparison.OrdinalIgnoreCase) ? "ENG" : "DE";
            masterVolume = Math.Clamp(acc.MasterVolume, 0f, 1f);
            bloodEffects = acc.BloodEffects;
            screenShakeEnabled = acc.ScreenShakeEnabled;
            highParticles = acc.HighParticles;
            showCrosshair = acc.ShowCrosshair;
            crosshairStyleIndex = Math.Clamp(acc.CrosshairStyleIndex, 0, 3);
            crosshairColorIndex = Math.Clamp(acc.CrosshairColorIndex, 0, crosshairPalette.Length - 1);
            crosshairSizeLevel = Math.Clamp(acc.CrosshairSizeLevel, 1, 3);
            lobbyMusicEnabled = acc.LobbyMusicEnabled;
            selectedLobbyTrack = Math.Clamp(acc.SelectedLobbyTrack, 0, 3);
            autoReload = acc.AutoReload;

            coins = Math.Max(0, acc.Coins);
            totalKills = Math.Max(0, acc.TotalKills);
            totalScore = Math.Max(0, acc.TotalScore);
            bestWave = Math.Max(1, acc.BestWave);
            gamesPlayed = Math.Max(0, acc.GamesPlayed);

            battlePassXp = Math.Max(0, acc.BattlePassXp);
            CopyBoolArray(acc.ClaimedPassTiers, claimedPassTiers, defaultFirstTrue: false);

            fireRateLevel = Math.Clamp(acc.FireRateLevel, 1, 50);
            speedLevel = Math.Clamp(acc.SpeedLevel, 1, 50);
            maxHpLevel = Math.Clamp(acc.MaxHpLevel, 1, 50);
            staminaRegenLevel = Math.Clamp(acc.StaminaRegenLevel, 1, 50);
            coinBoostLevel = Math.Clamp(acc.CoinBoostLevel, 1, 50);
            bonusArmorLevel = Math.Clamp(acc.BonusArmorLevel, 0, 20);

            CopyBoolArray(acc.OwnedSkins, ownedSkins, defaultFirstTrue: true);
            equippedSkin = Math.Clamp(acc.EquippedSkin, 0, skinNamesDe.Length - 1);
            if (!ownedSkins[equippedSkin]) equippedSkin = 0;

            CopyBoolArray(acc.OwnedWeapons, ownedWeapons, defaultFirstTrue: true);
            CopyIntArray(acc.WeaponLevels, weaponLevels, minVal: 1, maxVal: 50);
            CopyIntArray(acc.WeaponMagBonus, weaponMagBonus, minVal: 0, maxVal: 100);
            equippedWeapon = Math.Clamp(acc.EquippedWeapon, 0, weaponNamesDe.Length - 1);
            if (!ownedWeapons[equippedWeapon]) equippedWeapon = 0;

            CopyBoolArray(acc.OwnedGear, ownedGear, defaultFirstTrue: true);
            equippedGear = Math.Clamp(acc.EquippedGear, 0, gearNamesDe.Length - 1);
            if (!ownedGear[equippedGear]) equippedGear = 0;

            CopyBoolArray(acc.OwnedLobbyMusic, ownedLobbyMusic, defaultFirstTrue: true);
            if (!ownedLobbyMusic[selectedLobbyTrack]) selectedLobbyTrack = 0;

            ApplyAudioAndSync();
            accountsDb.LastLoggedInUser = acc.Username;
            SaveAccountsDatabase();
        }

        private void LogoutToAuth()
        {
            SaveAccountsDatabase();
            currentAccount = null;
            mpView = MultiplayerView.None;
            isMultiplayerMatchActive = false;
            authPassword = "";
            authMessage = T("Ausgeloggt. Bitte einloggen oder registrieren.", "Logged out. Please log in or register.");
            authMessageIsError = false;
            authFocusField = 0;
            state = GameState.Auth;
            SyncCombatCursorVisibility();
            UpdateLobbyMusicState();
            SyncDiscordPresence(force: true);
        }

        private void SubmitAuthForm()
        {
            string user = authUsername.Trim();
            string pass = authPassword;

            if (user.Length < 3)
            {
                authMessage = T("Benutzername muss mindestens 3 Zeichen haben.", "Username must be at least 3 characters.");
                authMessageIsError = true;
                return;
            }

            if (IsGamertagBlocked(user, out string reason))
            {
                authMessage = reason;
                authMessageIsError = true;
                return;
            }

            if (pass.Length < 4)
            {
                authMessage = T("Passwort muss mindestens 4 Zeichen haben.", "Password must be at least 4 characters.");
                authMessageIsError = true;
                return;
            }

            string passHash = HashPassword(pass);

            if (authMode == AuthMode.Register)
            {
                bool exists = accountsDb.Accounts.Exists(a => a.Username.Equals(user, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    authMessage = T("Benutzername existiert bereits. Wechsle auf LOGIN.", "Username already exists. Switch to LOGIN.");
                    authMessageIsError = true;
                    return;
                }

                var newAcc = new UserAccount
                {
                    Username = user,
                    PasswordHash = passHash,
                    Coins = 450
                };
                accountsDb.Accounts.Add(newAcc);
                authPassword = "";
                authMessage = "";
                authMessageIsError = false;
                LoadAccountIntoGame(newAcc);
                state = GameState.MainMenu;
                SyncCombatCursorVisibility();
                UpdateLobbyMusicState();
                SyncDiscordPresence(force: true);
            }
            else
            {
                UserAccount? found = accountsDb.Accounts.Find(a => a.Username.Equals(user, StringComparison.OrdinalIgnoreCase));
                if (found == null || found.PasswordHash != passHash)
                {
                    authMessage = T("Benutzername oder Passwort falsch.", "Invalid username or password.");
                    authMessageIsError = true;
                    return;
                }

                authPassword = "";
                authMessage = "";
                authMessageIsError = false;
                LoadAccountIntoGame(found);
                state = GameState.MainMenu;
                SyncCombatCursorVisibility();
                UpdateLobbyMusicState();
                SyncDiscordPresence(force: true);
            }
        }

        private void SaveGameData() => SaveAccountsDatabase();

        private static void CopyBoolArray(bool[]? src, bool[] dst, bool defaultFirstTrue)
        {
            if (src == null) return;
            for (int i = 0; i < Math.Min(src.Length, dst.Length); i++)
                dst[i] = src[i];
            if (defaultFirstTrue && dst.Length > 0)
                dst[0] = true;
        }

        private static void CopyIntArray(int[]? src, int[] dst, int minVal, int maxVal)
        {
            if (src == null) return;
            for (int i = 0; i < Math.Min(src.Length, dst.Length); i++)
                dst[i] = Math.Clamp(src[i], minVal, maxVal);
        }

        private void SyncDiscordPresence(bool force = false)
        {
            if (discordRpc == null) return;

            if (!force && discordSyncTimer > 0)
            {
                discordSyncTimer--;
                return;
            }
            discordSyncTimer = 50;

            string tag = string.IsNullOrWhiteSpace(gamerTag) ? "Spieler" : gamerTag;
            string weaponInfo = $"{GetWeaponName(equippedWeapon)} Lv.{weaponLevels[equippedWeapon]}";

            switch (state)
            {
                case GameState.Loading:
                    discordRpc.UpdatePresence("Lädt FLOS GAME...", "Startet...", showTimer: false);
                    break;

                case GameState.Auth:
                    discordRpc.UpdatePresence(
                        authMode == AuthMode.Register ? "Registriert einen Account" : "Loggt sich ein",
                        "Login / Registrierung",
                        showTimer: false);
                    break;

                case GameState.MainMenu:
                    string tabText = mpView != MultiplayerView.None
                        ? $"In Multiplayer-Lobby ({currentRoomCode})"
                        : activeTab switch
                        {
                            MainTab.Skin => "Wählt einen Skin aus",
                            MainTab.Waffenkammer => "In der Waffenkammer",
                            MainTab.Karriere => $"Karriere (Rekord: Welle {bestWave})",
                            MainTab.Shop => "Im Shop",
                            MainTab.Spind => "Am Spind (Waffen, Skins & Musik)",
                            MainTab.Spin => "Dreht am Spin-Rad",
                            MainTab.BattlePass => $"Battle Pass (XP: {battlePassXp})",
                            MainTab.Einstellung => $"Einstellungen ({language})",
                            _ => "Im Hauptmenü"
                        };
                    discordRpc.UpdatePresence(
                        tabText,
                        $"{tag} • {weaponInfo} | {coins} Coins",
                        showTimer: false);
                    break;

                case GameState.Tutorial:
                    discordRpc.UpdatePresence(
                        $"Spielt Tutorial ({tutorialStep + 1}/4)",
                        $"Spieler: {tag}",
                        showTimer: false);
                    break;

                case GameState.Playing:
                case GameState.Paused:
                    string modePrefix = isMultiplayerMatchActive ? $"MP [{currentRoomCode}] • " : "";
                    discordRpc.UpdatePresence(
                        $"{modePrefix}Welle {wave} • {GetMapDisplayName(currentMap)}",
                        $"Kills: {kills} | HP: {playerHp}/{maxPlayerHp}",
                        showTimer: state == GameState.Playing,
                        smallImageKey: "weapon",
                        smallImageText: weaponInfo);
                    break;

                case GameState.GameOver:
                    discordRpc.UpdatePresence(
                        $"Game Over auf Welle {wave}",
                        $"Kills: {kills} | Punkte: {score}",
                        showTimer: false);
                    break;
            }
        }

        private void BuildSounds()
        {
            audioMixer = new WaveOutMixer { MasterGain = masterVolume };

            shootClip = LoadWavOrFallback(
                "freesound_community-pixel-shotgun-shoot-94300.wav",
                CreateToneSamples(780, 180, 90, 0.35f, 22050),
                22050);

            swimClip = LoadWavOrFallback(
                "floraphonic-underwater-movement-whoosh-3-186900.wav",
                CreateToneSamples(260, 145, 220, 0.25f, 22050),
                22050);

            grassRunClip = LoadWavOrFallback(
                "catfox_alex-running-in-wet-grass-235969.wav",
                CreateToneSamples(150, 85, 180, 0.22f, 22050),
                22050);

            hurtClip = LoadWavOrFallback(
                "homemade_sfx-ouch-oof-hurt-sound-effect-262616.wav",
                CreateToneSamples(195, 70, 180, 0.45f, 22050),
                22050);

            hitClip = new PcmClip(CreateToneSamples(220, 110, 60, 0.24f, 22050), 22050, false);
            waveClip = new PcmClip(CreateToneSamples(440, 880, 180, 0.30f, 22050), 22050, false);
            dashClip = new PcmClip(CreateToneSamples(320, 640, 110, 0.28f, 22050), 22050, false);
            reloadClip = new PcmClip(CreateToneSamples(520, 310, 120, 0.22f, 22050), 22050, false);
            pickupClip = new PcmClip(CreateToneSamples(660, 990, 95, 0.26f, 22050), 22050, false);
            spinTickClip = new PcmClip(CreateToneSamples(900, 620, 35, 0.20f, 22050), 22050, false);

            lobbyMusicClips[0] = new PcmClip(CreateLobbyLoopSamples(110, 165, 220, 1800, 0.35f, 22050), 22050, false);
            lobbyMusicClips[1] = new PcmClip(CreateLobbyLoopSamples(90, 135, 270, 1600, 0.42f, 22050), 22050, false);
            lobbyMusicClips[2] = new PcmClip(CreateLobbyLoopSamples(130, 195, 260, 1900, 0.34f, 22050), 22050, false);
            lobbyMusicClips[3] = new PcmClip(CreateLobbyLoopSamples(75, 150, 300, 1750, 0.38f, 22050), 22050, false);
        }

        private static float[] CreateLobbyLoopSamples(double f1, double f2, double f3, int durationMs, float vol, int rate)
        {
            int total = Math.Max(1, (rate * durationMs) / 1000);
            float[] arr = new float[total];
            double[] notes = { f1, f2, f3, f2 };

            for (int i = 0; i < total; i++)
            {
                int step = (i * notes.Length) / total;
                double freq = notes[step];
                double t = i / (double)rate;
                double beatPulse = 0.65 + 0.35 * Math.Sin(2.0 * Math.PI * 4.0 * t);
                arr[i] = (float)(Math.Sin(2.0 * Math.PI * freq * t) * beatPulse * vol);
            }

            return arr;
        }

        private static PcmClip LoadWavOrFallback(string fileName, float[] fallbackSamples, int fallbackRate)
        {
            string? path = ResolveAudioPath(fileName);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    byte[] raw = File.ReadAllBytes(path);
                    if (TryDecodeWavToMonoFloat(raw, out float[] decoded, out int rate) && decoded.Length > 0)
                        return new PcmClip(decoded, rate, true);
                }
                catch { }
            }

            return new PcmClip(fallbackSamples, fallbackRate, false);
        }

        private static string? ResolveAudioPath(string fileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName),
                fileName
            };

            foreach (string c in candidates)
            {
                if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
                    return Path.GetFullPath(c);
            }

            string prefix = Path.GetFileNameWithoutExtension(fileName);
            if (prefix.Length >= 24) prefix = prefix[..24];

            foreach (string dir in new[] { baseDir, Directory.GetCurrentDirectory() })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !fn.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                        !fn.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !fn.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        !fn.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        return f;
                    }
                }
            }

            return null;
        }

        private static bool TryDecodeWavToMonoFloat(byte[] data, out float[] mono, out int sampleRate)
        {
            mono = Array.Empty<float>();
            sampleRate = 22050;
            if (data.Length < 44) return false;

            if (Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
                return false;

            ushort audioFormat = 0;
            ushort channels = 1;
            uint rate = 22050;
            ushort bitsPerSample = 16;
            byte[]? sampleData = null;

            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                string chunkId = Encoding.ASCII.GetString(data, pos, 4);
                int chunkSize = BitConverter.ToInt32(data, pos + 4);
                if (chunkSize < 0 || pos + 8 + chunkSize > data.Length) break;

                int bodyStart = pos + 8;
                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    audioFormat = BitConverter.ToUInt16(data, bodyStart);
                    channels = BitConverter.ToUInt16(data, bodyStart + 2);
                    rate = BitConverter.ToUInt32(data, bodyStart + 4);
                    bitsPerSample = BitConverter.ToUInt16(data, bodyStart + 14);
                }
                else if (chunkId == "data")
                {
                    sampleData = new byte[chunkSize];
                    Buffer.BlockCopy(data, bodyStart, sampleData, 0, chunkSize);
                }

                pos += 8 + chunkSize + (chunkSize % 2);
            }

            if (sampleData == null || sampleData.Length == 0 || channels == 0 || rate == 0)
                return false;

            float[] interleaved;
            if (audioFormat == 1 && bitsPerSample == 16)
            {
                int count = sampleData.Length / 2;
                interleaved = new float[count];
                for (int i = 0; i < count; i++)
                    interleaved[i] = BitConverter.ToInt16(sampleData, i * 2) / 32768f;
            }
            else if (audioFormat == 1 && bitsPerSample == 8)
            {
                interleaved = new float[sampleData.Length];
                for (int i = 0; i < sampleData.Length; i++)
                    interleaved[i] = (sampleData[i] - 128) / 128f;
            }
            else if (audioFormat == 1 && bitsPerSample == 24)
            {
                int count = sampleData.Length / 3;
                interleaved = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int idx = i * 3;
                    int val = sampleData[idx] | (sampleData[idx + 1] << 8) | (sampleData[idx + 2] << 16);
                    if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000);
                    interleaved[i] = val / 8388608f;
                }
            }
            else if (audioFormat == 3 && bitsPerSample == 32)
            {
                int count = sampleData.Length / 4;
                interleaved = new float[count];
                for (int i = 0; i < count; i++)
                    interleaved[i] = Math.Clamp(BitConverter.ToSingle(sampleData, i * 4), -1f, 1f);
            }
            else if (audioFormat == 1 && bitsPerSample == 32)
            {
                int count = sampleData.Length / 4;
                interleaved = new float[count];
                for (int i = 0; i < count; i++)
                    interleaved[i] = BitConverter.ToInt32(sampleData, i * 4) / 2147483648f;
            }
            else
            {
                return false;
            }

            int frames = interleaved.Length / channels;
            mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[f * channels + c];
                mono[f] = sum / channels;
            }

            sampleRate = (int)rate;
            return frames > 0;
        }

        private static float[] CreateToneSamples(double startFreq, double endFreq, int durationMs, float volume, int rate)
        {
            int totalSamples = Math.Max(1, (rate * durationMs) / 1000);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = i / (double)totalSamples;
                double freq = startFreq + (endFreq - startFreq) * t;
                double envelope = 1.0 - t;
                samples[i] = (float)(Math.Sin(2.0 * Math.PI * freq * (i / (double)rate)) * envelope * volume);
            }

            return samples;
        }

        private float GetRiverCenterAt(float alongPos)
        {
            float baseCenter = horizontalRiver ? riverCenterY : riverCenterX;
            return baseCenter + MathF.Sin(alongPos * 0.009f + riverCurveSeed) * 26f;
        }

        private void GenerateMapEnvironment(MapType map)
        {
            currentMap = map;
            int w = Math.Max(400, this.ClientSize.Width);
            int h = Math.Max(300, this.ClientSize.Height);

            horizontalRiver = (map == MapType.WaldBach || map == MapType.SteppeFluss);
            riverCenterX = w * 0.50f;
            riverCenterY = h * 0.54f;
            riverCurveSeed = (float)(rng.NextDouble() * Math.PI * 2);
            riverWidth = map switch
            {
                MapType.WaldBach => 78f,
                MapType.SumpfKanal => 98f,
                _ => 114f
            };

            grassTufts.Clear();
            Color[] palette = map switch
            {
                MapType.RasenFluss => new[] { Color.FromArgb(46, 122, 48), Color.FromArgb(68, 148, 58), Color.FromArgb(36, 98, 40) },
                MapType.WaldBach => new[] { Color.FromArgb(26, 74, 34), Color.FromArgb(38, 94, 42), Color.FromArgb(20, 58, 28) },
                MapType.SumpfKanal => new[] { Color.FromArgb(72, 88, 40), Color.FromArgb(92, 108, 48), Color.FromArgb(54, 68, 32) },
                _ => new[] { Color.FromArgb(124, 130, 58), Color.FromArgb(144, 146, 66), Color.FromArgb(98, 102, 46) }
            };

            for (int i = 0; i < 230; i++)
            {
                grassTufts.Add(new GrassTuft
                {
                    X = rng.Next(8, w - 8),
                    Y = rng.Next(8, h - 8),
                    W = rng.Next(6, 15),
                    H = rng.Next(4, 10),
                    Color = palette[rng.Next(palette.Length)],
                    SwayPhase = (float)(rng.NextDouble() * Math.PI * 2)
                });
            }

            mapProps.Clear();
            int propCount = map switch
            {
                MapType.WaldBach => 30,
                MapType.SumpfKanal => 24,
                MapType.SteppeFluss => 22,
                _ => 24
            };

            for (int i = 0; i < propCount; i++)
            {
                float px = rng.Next(35, w - 35);
                float py = rng.Next(85, h - 35);

                float distCenter = MathF.Sqrt((px - w * 0.5f) * (px - w * 0.5f) + (py - h * 0.5f) * (py - h * 0.5f));
                if (distCenter < 75f) continue;

                bool inWater = IsInRiver(px, py);

                if (inWater)
                {
                    if (map == MapType.SumpfKanal || rng.NextDouble() < 0.65)
                    {
                        mapProps.Add(new MapProp
                        {
                            Kind = PropKind.Reed,
                            X = px,
                            Y = py,
                            Radius = rng.Next(8, 14),
                            PrimaryColor = Color.FromArgb(96, 132, 54),
                            SecondaryColor = Color.FromArgb(132, 162, 68),
                            SwayPhase = (float)(rng.NextDouble() * Math.PI * 2)
                        });
                    }
                    continue;
                }

                PropKind kind = map switch
                {
                    MapType.WaldBach => rng.NextDouble() < 0.65 ? PropKind.Tree : PropKind.Bush,
                    MapType.SumpfKanal => rng.NextDouble() < 0.55 ? PropKind.Reed : PropKind.Bush,
                    MapType.SteppeFluss => rng.NextDouble() < 0.65 ? PropKind.Rock : PropKind.Bush,
                    _ => rng.NextDouble() < 0.45 ? PropKind.Tree : (rng.NextDouble() < 0.75 ? PropKind.Bush : PropKind.Rock)
                };

                mapProps.Add(kind switch
                {
                    PropKind.Tree => new MapProp
                    {
                        Kind = PropKind.Tree,
                        X = px,
                        Y = py,
                        Radius = rng.Next(18, 28),
                        PrimaryColor = map == MapType.WaldBach ? Color.FromArgb(24, 68, 32) : Color.FromArgb(34, 98, 42),
                        SecondaryColor = map == MapType.WaldBach ? Color.FromArgb(52, 114, 54) : Color.FromArgb(72, 146, 66),
                        SwayPhase = (float)(rng.NextDouble() * Math.PI * 2)
                    },
                    PropKind.Bush => new MapProp
                    {
                        Kind = PropKind.Bush,
                        X = px,
                        Y = py,
                        Radius = rng.Next(11, 17),
                        PrimaryColor = Color.FromArgb(44, 108, 46),
                        SecondaryColor = Color.FromArgb(84, 158, 72),
                        SwayPhase = (float)(rng.NextDouble() * Math.PI * 2)
                    },
                    PropKind.Reed => new MapProp
                    {
                        Kind = PropKind.Reed,
                        X = px,
                        Y = py,
                        Radius = rng.Next(8, 13),
                        PrimaryColor = Color.FromArgb(96, 132, 54),
                        SecondaryColor = Color.FromArgb(145, 178, 82),
                        SwayPhase = (float)(rng.NextDouble() * Math.PI * 2)
                    },
                    _ => new MapProp
                    {
                        Kind = PropKind.Rock,
                        X = px,
                        Y = py,
                        Radius = rng.Next(10, 17),
                        PrimaryColor = Color.FromArgb(98, 102, 108),
                        SecondaryColor = Color.FromArgb(168, 172, 178),
                        SwayPhase = 0f
                    }
                });
            }
        }

        private void SwitchToNextMap(string reasonBanner, bool withTransition)
        {
            int nextIndex = (((int)currentMap) + 1) % Enum.GetValues<MapType>().Length;
            MapType nextMap = (MapType)nextIndex;

            if (withTransition)
            {
                mapTransitionProgress = 0.01f;
                mapTransitionSwapped = false;
                mapBannerText = $"{reasonBanner} -> {GetMapDisplayName(nextMap)}";
                mapBannerTicks = 120;
            }
            else
            {
                GenerateMapEnvironment(nextMap);
                bloodSplatters.Clear();
                terrainPixels.Clear();
                shellCasings.Clear();
                muzzleFlashes.Clear();
                sparkParticles.Clear();
                mapBannerText = T($"Runde {wave}: {GetMapDisplayName(nextMap)}", $"Wave {wave}: {GetMapDisplayName(nextMap)}");
                mapBannerTicks = 100;
            }
        }

        private string GetMapDisplayName(MapType map) => map switch
        {
            MapType.RasenFluss => T("Rasen & Fluss", "Grass & River"),
            MapType.WaldBach => T("Dunkler Wald & Bach", "Dark Forest & Creek"),
            MapType.SumpfKanal => T("Sumpf & Kanal", "Swamp & Canal"),
            _ => T("Steppe & Flussbett", "Steppe & Riverbed")
        };

        private void UpdateLayoutRects()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            int authW = Math.Min(440, w - 60);
            int authX = (w - authW) / 2;
            int authY = Math.Max(90, (h - 360) / 2);
            authModeLoginRect = new Rectangle(authX, authY, authW / 2 - 6, 38);
            authModeRegisterRect = new Rectangle(authX + authW / 2 + 6, authY, authW / 2 - 6, 38);
            authUserBoxRect = new Rectangle(authX, authY + 86, authW, 42);
            authPassBoxRect = new Rectangle(authX, authY + 162, authW, 42);
            authSubmitRect = new Rectangle(authX, authY + 224, authW, 48);

            logoutBtnRect = new Rectangle(w - 315, 22, 100, 42);

            int margin = 22;
            int gap = 5;
            int tabW = Math.Max(70, (w - margin * 2 - gap * 8) / 9);
            for (int i = 0; i < 9; i++)
                mainTabRects[i] = new Rectangle(margin + i * (tabW + gap), 82, tabW, 38);

            int subGap = 10;
            int subW = Math.Max(110, (w - 60 - subGap * 3) / 4);
            for (int i = 0; i < 4; i++)
            {
                shopSubTabRects[i] = new Rectangle(30 + i * (subW + subGap), 140, subW, 36);
                spindLockerTabRects[i] = new Rectangle(30 + i * (subW + subGap), 140, subW, 36);
            }

            tagBoxRect = new Rectangle(30, 200, 310, 44);
            startBtnRect = new Rectangle(30, 262, 310, 52);
            multiplayerBtnRect = new Rectangle(30, 324, 310, 48);

            int mpW = Math.Min(720, w - 60);
            int mpH = Math.Min(430, h - 130);
            int mpX = (w - mpW) / 2;
            int mpY = Math.Max(80, (h - mpH) / 2);
            mpCloseBtnRect = new Rectangle(mpX + mpW - 44, mpY + 12, 32, 32);
            mpCreateLobbyBtnRect = new Rectangle(mpX + 40, mpY + 135, mpW - 80, 54);
            mpJoinByCodeBtnRect = new Rectangle(mpX + 40, mpY + 206, mpW - 80, 54);

            mpCodeInputRect = new Rectangle(mpX + 40, mpY + 145, mpW - 80, 48);
            mpConfirmJoinBtnRect = new Rectangle(mpX + 40, mpY + 214, 240, 48);
            mpBackToSelectBtnRect = new Rectangle(mpX + 300, mpY + 214, 200, 48);

            int rightPanelX = mpX + (int)(mpW * 0.56f);
            int rightPanelW = mpX + mpW - rightPanelX - 24;
            mpCopyCodeBtnRect = new Rectangle(rightPanelX, mpY + 168, rightPanelW, 38);
            mpStartMatchBtnRect = new Rectangle(rightPanelX, mpY + 224, rightPanelW, 52);
            mpLeaveLobbyBtnRect = new Rectangle(rightPanelX, mpY + 290, rightPanelW, 42);

            int catW = (w - 84) / 3;
            for (int i = 0; i < 3; i++)
                settingsCatRects[i] = new Rectangle(30 + i * (catW + 12), 144, catW, 36);

            setBtnA = new Rectangle(30, 212, 240, 42);
            setBtnB = new Rectangle(300, 212, 240, 42);
            setBtnC = new Rectangle(30, 296, 240, 42);
            setBtnD = new Rectangle(300, 296, 240, 42);
            setBtnE = new Rectangle(30, 380, 240, 42);
            setBtnF = new Rectangle(300, 380, 240, 42);

            int wheelSize = Math.Min(230, Math.Max(160, h - 320));
            spinWheelRect = new Rectangle(42, 165, wheelSize, wheelSize);
            spinBtnRect = new Rectangle(305, 200, 260, 52);

            int boxW = Math.Min(540, w - 80);
            int boxH = 260;
            int boxX = (w - boxW) / 2;
            int boxY = (h - boxH) / 2;
            tutNextRect = new Rectangle(boxX + boxW - 185, boxY + boxH - 52, 160, 36);
            tutSkipRect = new Rectangle(boxX + 25, boxY + boxH - 52, 140, 36);

            pauseResumeRect = new Rectangle(w / 2 - 150, h / 2 + 10, 300, 44);
            pauseMenuRect = new Rectangle(w / 2 - 150, h / 2 + 66, 300, 44);
        }

        private void BuildCardsGrid(int count, int topY = 192, int cardH = 155, int cols = 3)
        {
            currentCardRects.Clear();
            int w = this.ClientSize.Width;
            int margin = 30;
            int gap = 12;
            int cardW = Math.Max(160, (w - margin * 2 - gap * (cols - 1)) / cols);

            for (int i = 0; i < count; i++)
            {
                int r = i / cols;
                int c = i % cols;
                currentCardRects.Add(new Rectangle(margin + c * (cardW + gap), topY + r * (cardH + gap), cardW, cardH));
            }
        }

        private void BuildStars()
        {
            stars.Clear();
            for (int i = 0; i < 160; i++)
            {
                stars.Add(new Star
                {
                    X = rng.Next(0, 1920),
                    Y = rng.Next(0, 1080),
                    Size = rng.Next(2, 5),
                    Phase = (float)(rng.NextDouble() * Math.PI * 2),
                    Speed = 0.08f + (float)(rng.NextDouble() * 0.14f)
                });
            }
        }

        private void BuildCharacterRigs()
        {
            for (int i = 0; i < humanRigs.Length; i++)
                humanRigs[i]?.Dispose();
            zombieRig?.Dispose();

            Color[] jacketMain =
            {
                Color.FromArgb(44, 118, 214),
                Color.FromArgb(38, 196, 108),
                Color.FromArgb(208, 54, 48),
                Color.FromArgb(72, 54, 128),
                Color.FromArgb(218, 168, 28)
            };
            Color[] jacketLight =
            {
                Color.FromArgb(98, 168, 250),
                Color.FromArgb(104, 238, 158),
                Color.FromArgb(245, 112, 98),
                Color.FromArgb(126, 98, 185),
                Color.FromArgb(252, 215, 92)
            };
            Color[] jacketDark =
            {
                Color.FromArgb(24, 68, 134),
                Color.FromArgb(20, 118, 62),
                Color.FromArgb(128, 26, 24),
                Color.FromArgb(38, 28, 74),
                Color.FromArgb(148, 106, 14)
            };

            for (int s = 0; s < 5; s++)
            {
                Color jMain = jacketMain[s];
                Color jLight = jacketLight[s];
                Color jDark = jacketDark[s];
                Color hairMain = s == 3 ? Color.FromArgb(26, 24, 36) : (s == 4 ? Color.FromArgb(92, 68, 28) : Color.FromArgb(64, 46, 32));
                Color hairLight = s == 3 ? Color.FromArgb(62, 58, 82) : (s == 4 ? Color.FromArgb(145, 112, 48) : Color.FromArgb(108, 80, 56));
                Color skinMain = Color.FromArgb(236, 186, 148);
                Color skinShade = Color.FromArgb(198, 146, 112);

                humanRigs[s] = CreateRig(
                    bodyGrid: new[]
                    {
                        "....LLLLLL....",
                        "...LHHHHHH....",
                        "...HHHHHHHH...",
                        "...SSSSSSSS...",
                        "...SSEESSES...",
                        "...SSUMMUSS...",
                        "....SSSSSS....",
                        "...LJJJJJJD...",
                        "...LJJJJJJD...",
                        "...LJJJJJJD..."
                    },
                    legGrid: new[]
                    {
                        "PP",
                        "PD",
                        "BB"
                    },
                    armGrid: new[]
                    {
                        "SL",
                        "JD",
                        "SS"
                    },
                    map: c => c switch
                    {
                        'L' => hairLight,
                        'H' => hairMain,
                        'S' => skinMain,
                        'U' => skinShade,
                        'E' => Color.FromArgb(24, 34, 48),
                        'M' => Color.FromArgb(168, 96, 86),
                        'J' => jMain,
                        'D' => jDark,
                        'P' => Color.FromArgb(46, 58, 78),
                        'B' => Color.FromArgb(32, 26, 22),
                        _ => Color.Transparent
                    });

                if (humanRigs[s]?.BodyAndHead != null)
                {
                    Bitmap b = humanRigs[s]!.BodyAndHead!;
                    b.SetPixel(3, 7, jLight);
                    b.SetPixel(4, 7, jLight);
                    b.SetPixel(9, 7, jDark);
                }
            }

            zombieRig = CreateRig(
                bodyGrid: new[]
                {
                    "....GGGGGG....",
                    "...GGGGGGGG...",
                    "...ZZZZZZZZ...",
                    "...ZZRZZZRZ...",
                    "...ZZZZZZUZ...",
                    "...ZZMMZZUZ...",
                    "....ZZZZZZ....",
                    "...TDDDTDD....",
                    "...TDDDTDD....",
                    "...TDDDTDD...."
                },
                legGrid: new[]
                {
                    "PD",
                    "PD",
                    "BB"
                },
                armGrid: new[]
                {
                    "ZG",
                    "TD",
                    "ZR"
                },
                map: c => c switch
                {
                    'G' => Color.FromArgb(58, 96, 44),
                    'Z' => Color.FromArgb(116, 174, 88),
                    'U' => Color.FromArgb(82, 132, 62),
                    'R' => Color.FromArgb(178, 32, 32),
                    'M' => Color.FromArgb(112, 24, 24),
                    'T' => Color.FromArgb(74, 82, 64),
                    'D' => Color.FromArgb(46, 52, 42),
                    'P' => Color.FromArgb(44, 52, 64),
                    'B' => Color.FromArgb(30, 24, 20),
                    _ => Color.Transparent
                });
        }

        private static CharacterRig CreateRig(string[] bodyGrid, string[] legGrid, string[] armGrid, Func<char, Color> map)
        {
            Bitmap body = CreateSpriteFromGrid(bodyGrid, map);
            Bitmap leg = CreateSpriteFromGrid(legGrid, map);
            Bitmap arm = CreateSpriteFromGrid(armGrid, map);

            var preview = new Bitmap(18, 15);
            using (Graphics pg = Graphics.FromImage(preview))
            {
                pg.InterpolationMode = InterpolationMode.NearestNeighbor;
                pg.PixelOffsetMode = PixelOffsetMode.Half;
                pg.DrawImage(leg, 6, 10, 2, 4);
                pg.DrawImage(leg, 10, 10, 2, 4);
                pg.DrawImage(body, 3, 1, 12, 10);
                pg.DrawImage(arm, 1, 6, 2, 4);
                pg.DrawImage(arm, 15, 6, 2, 4);
            }

            return new CharacterRig
            {
                FullPreview = preview,
                BodyAndHead = body,
                LeftLeg = leg,
                RightLeg = (Bitmap)leg.Clone(),
                LeftArm = arm,
                RightArm = (Bitmap)arm.Clone()
            };
        }

        private static Bitmap CreateSpriteFromGrid(string[] rows, Func<char, Color> map)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            var bmp = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    bmp.SetPixel(x, y, map(rows[y][x]));
            }

            return bmp;
        }

        private bool IsGamertagBlocked(string tag, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(tag))
            {
                reason = T("Bitte einen Namen eingeben.", "Please enter a name.");
                return true;
            }

            string normalized = tag.ToLowerInvariant()
                .Replace("0", "o").Replace("1", "i").Replace("3", "e")
                .Replace("4", "a").Replace("@", "a").Replace("$", "s");

            foreach (string bad in blacklistedWords)
            {
                if (normalized.Contains(bad))
                {
                    reason = T($"Name blockiert: '{bad}' ist nicht erlaubt.", $"Name blocked: '{bad}' is not allowed.");
                    return true;
                }
            }

            return false;
        }

        private int GetWeaponDamage(int wIndex)
        {
            int lv = weaponLevels[wIndex];
            return wIndex switch
            {
                0 => lv == 1 ? 1 : 1 + (lv / 2),
                1 => 1 + ((lv - 1) / 2),
                2 => 2 + (lv - 1),
                3 => 2 + ((lv * 3) / 2),
                _ => 1
            };
        }

        private int GetShootCooldownMax()
        {
            int lv = weaponLevels[equippedWeapon];
            int baseCd = equippedWeapon switch
            {
                0 => 16,
                1 => 7,
                2 => 18,
                3 => 9,
                _ => 14
            };
            return Math.Max(2, baseCd - (lv - 1) * 2 - (fireRateLevel - 1) * 2);
        }

        private int GetWeaponMagCapacity(int wIndex) => weaponMagSizes[wIndex] + (weaponLevels[wIndex] - 1) * 2 + weaponMagBonus[wIndex] * 4;
        private int GetWeaponMagUpgradeCost(int wIndex) => 130 + weaponMagBonus[wIndex] * 90;
        private int GetWeaponUpgradeCost(int wIndex) => (weaponPrices[wIndex] / 2 + 120) * weaponLevels[wIndex];
        private float GetPlayerSpeed() => 4.1f + (speedLevel - 1) * 0.60f;
        private float GetCoinMultiplier() => 1.0f + (coinBoostLevel - 1) * 0.25f;

        private int GetMaxHpValue()
        {
            int gearHp = equippedGear switch { 1 => 30, 2 => 50, 3 => 85, _ => 0 };
            return 100 + (maxHpLevel - 1) * 25 + gearHp;
        }

        private float GetDamageReduction()
        {
            float baseGear = equippedGear switch { 1 => 0.78f, 2 => 0.70f, 3 => 0.55f, _ => 1.0f };
            float extraArmor = MathF.Pow(0.95f, bonusArmorLevel);
            return baseGear * extraArmor;
        }

        private int GetUpgradeCost(int level) => level * 110;

        private int GetBattlePassLevel() => 1 + (battlePassXp / XpPerTier);

        private void GrantBattlePassXp(int amount)
        {
            if (amount <= 0) return;
            battlePassXp += amount;
        }

        private void StartSpin()
        {
            if (isSpinning || coins < SpinCost) return;
            coins -= SpinCost;
            isSpinning = true;
            spinVelocity = 24f + (float)(rng.NextDouble() * 14f);
            lastSpinResultText = T("Dreht...", "Spinning...");
            SaveGameData();
        }

        private void UpdateSpin()
        {
            if (!isSpinning) return;

            float prevSector = MathF.Floor(spinAngle / 45f);
            spinAngle = (spinAngle + spinVelocity) % 360f;
            float currSector = MathF.Floor(spinAngle / 45f);
            if (prevSector != currSector)
                audioMixer?.PlayOneShot(spinTickClip, gain: 0.55f);

            spinVelocity *= 0.984f;
            if (spinVelocity <= 0.14f)
            {
                isSpinning = false;
                spinVelocity = 0f;
                int sectorCount = spinPrizes.Count;
                float sliceDeg = 360f / sectorCount;
                float pointerAngle = (360f - (spinAngle % 360f) + 270f) % 360f;
                int idx = Math.Clamp((int)(pointerAngle / sliceDeg), 0, sectorCount - 1);
                selectedSpinPrizeIdx = idx;
                ApplySpinPrize(spinPrizes[idx]);
                SaveGameData();
                SyncDiscordPresence(force: true);
            }
        }

        private void ApplySpinPrize(SpinPrize prize)
        {
            if (prize.Kind == SpinRewardKind.WeaponUnlockOrLevel)
            {
                int w = Math.Clamp(prize.Index, 0, weaponNamesDe.Length - 1);
                if (!ownedWeapons[w])
                {
                    ownedWeapons[w] = true;
                    equippedWeapon = w;
                    lastSpinResultText = T($"GEWONNEN: {GetWeaponName(w)} freigeschaltet!", $"WON: {GetWeaponName(w)} unlocked!");
                }
                else
                {
                    weaponLevels[w]++;
                    equippedWeapon = w;
                    lastSpinResultText = T($"GEWONNEN: {GetWeaponName(w)} -> Stufe {weaponLevels[w]}!", $"WON: {GetWeaponName(w)} -> Lv.{weaponLevels[w]}!");
                }
            }
            else if (prize.Kind == SpinRewardKind.SkinUnlock)
            {
                int s = Math.Clamp(prize.Index, 0, skinNamesDe.Length - 1);
                if (!ownedSkins[s])
                {
                    ownedSkins[s] = true;
                    equippedSkin = s;
                    lastSpinResultText = T($"GEWONNEN: Skin '{GetSkinName(s)}' freigeschaltet!", $"WON: Skin '{GetSkinName(s)}' unlocked!");
                }
                else
                {
                    equippedSkin = s;
                    coins += 100;
                    lastSpinResultText = T($"Skin '{GetSkinName(s)}' ausgerüstet + 100 Coins!", $"Skin '{GetSkinName(s)}' equipped + 100 Coins!");
                }
            }
            else if (prize.Kind == SpinRewardKind.LobbyMusicUnlock)
            {
                int m = Math.Clamp(prize.Index, 0, lobbyTrackNamesDe.Length - 1);
                if (!ownedLobbyMusic[m])
                {
                    ownedLobbyMusic[m] = true;
                    selectedLobbyTrack = m;
                    UpdateLobbyMusicState();
                    lastSpinResultText = T($"GEWONNEN: Lobbymusik '{GetLobbyTrackName(m)}'!", $"WON: Lobby Music '{GetLobbyTrackName(m)}'!");
                }
                else
                {
                    selectedLobbyTrack = m;
                    UpdateLobbyMusicState();
                    coins += 90;
                    lastSpinResultText = T($"Lobbymusik '{GetLobbyTrackName(m)}' aktiv + 90 Coins!", $"Lobby Music '{GetLobbyTrackName(m)}' active + 90 Coins!");
                }
            }
        }

        private void ClaimBattlePassReward(int idx)
        {
            if (idx < 0 || idx >= battlePassRewards.Count) return;
            BattlePassReward rew = battlePassRewards[idx];
            if (claimedPassTiers[idx] || GetBattlePassLevel() < rew.Tier) return;

            claimedPassTiers[idx] = true;
            coins += rew.RewardCoins;

            if (rew.UnlockSkin.HasValue)
            {
                int s = Math.Clamp(rew.UnlockSkin.Value, 0, skinNamesDe.Length - 1);
                ownedSkins[s] = true;
                equippedSkin = s;
            }
            if (rew.UnlockWeapon.HasValue)
            {
                int w = Math.Clamp(rew.UnlockWeapon.Value, 0, weaponNamesDe.Length - 1);
                ownedWeapons[w] = true;
                equippedWeapon = w;
            }
            if (rew.UnlockMusic.HasValue)
            {
                int m = Math.Clamp(rew.UnlockMusic.Value, 0, lobbyTrackNamesDe.Length - 1);
                ownedLobbyMusic[m] = true;
                selectedLobbyTrack = m;
                UpdateLobbyMusicState();
            }

            audioMixer?.PlayOneShot(pickupClip, gain: 0.85f);
            SaveGameData();
            SyncDiscordPresence(force: true);
        }

        private void StartReload()
        {
            int cap = GetWeaponMagCapacity(equippedWeapon);
            if (reloadTimer > 0 || currentAmmo >= cap) return;
            reloadDuration = Math.Max(14, weaponReloadTicks[equippedWeapon] - (fireRateLevel - 1) * 2);
            reloadTimer = reloadDuration;
            audioMixer?.PlayOneShot(reloadClip, gain: 0.70f);
        }

        private void TriggerDash(float moveDx, float moveDy)
        {
            if (dashTimer > 0 || dashCooldown > 0 || isExhausted || stamina < 18f) return;

            stamina = Math.Max(0f, stamina - 18f);
            if (stamina <= 0.1f) isExhausted = true;

            float len = MathF.Sqrt(moveDx * moveDx + moveDy * moveDy);
            if (len > 0.01f)
            {
                dashDirX = moveDx / len;
                dashDirY = moveDy / len;
            }
            else
            {
                dashDirX = MathF.Cos(playerAngle);
                dashDirY = MathF.Sin(playerAngle);
            }

            dashTimer = 9;
            dashCooldown = 36;
            AddShake(5.5f);
            SpawnSparks(playerX, playerY, Color.Cyan, highParticles ? 10 : 5);
            audioMixer?.PlayOneShot(dashClip, gain: 0.80f);
        }

        private void AddShake(float amount)
        {
            if (!screenShakeEnabled) return;
            screenShakeMag = Math.Min(14f, Math.Max(screenShakeMag, amount));
        }

        private void ResetPlayerAndWorld()
        {
            audioMixer?.StopMovementLoop();
            audioMixer?.StopLobbyMusicLoop();

            playerX = this.ClientSize.Width / 2f;
            playerY = this.ClientSize.Height / 2f;
            maxPlayerHp = GetMaxHpValue();
            playerHp = maxPlayerHp;
            stamina = MaxStamina;
            isExhausted = false;
            isMovingNow = false;
            isSprintingNow = false;
            isSwimmingNow = false;
            walkAnimPhase = 0f;
            terrainParticleTimer = 0;
            gunRecoil = 0f;
            screenShakeMag = 0f;
            dashTimer = 0;
            dashCooldown = 0;

            currentAmmo = GetWeaponMagCapacity(equippedWeapon);
            reloadTimer = 0;
            reloadDuration = weaponReloadTicks[equippedWeapon];

            wave = 1;
            zombiesToSpawnInWave = 6;
            spawnCooldown = 20;
            score = 0;
            kills = 0;
            damageFlashAlpha = 0f;
            runElapsedSeconds = 0f;
            nextFiveMinuteIndex = 1;
            mapTransitionProgress = 0f;
            mapTransitionSwapped = false;

            bullets.Clear();
            shellCasings.Clear();
            muzzleFlashes.Clear();
            sparkParticles.Clear();
            pickups.Clear();
            zombies.Clear();
            bloodParticles.Clear();
            bloodSplatters.Clear();
            terrainPixels.Clear();
            activeCoOpMates.Clear();

            GenerateMapEnvironment(MapType.RasenFluss);
            mapBannerText = T($"Runde 1: {GetMapDisplayName(currentMap)}", $"Wave 1: {GetMapDisplayName(currentMap)}");
            mapBannerTicks = 110;
        }

        private void RecordRunStats()
        {
            gamesPlayed++;
            totalKills += kills;
            totalScore += score;
            if (wave > bestWave) bestWave = wave;
            GrantBattlePassXp(kills * 12 + wave * 45);
            SaveGameData();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            SyncCombatCursorVisibility();

            if (state == GameState.Loading)
                UpdateLoadingScreen();
            else if (state == GameState.Playing)
                UpdatePlaying();
            else
            {
                audioMixer?.StopMovementLoop();
                if (state == GameState.MainMenu && activeTab == MainTab.Spin)
                    UpdateSpin();
                UpdateLobbyMusicState();
            }

            SyncDiscordPresence();
            this.Invalidate();
        }

        private void UpdateLoadingScreen()
        {
            loadProgress += 0.85f;
            spinnerAngle = (spinnerAngle + 11f) % 360f;
            loadingFxPhase += 0.07f;

            foreach (Star s in stars)
                s.Phase += s.Speed;

            tipTicks++;
            if (tipTicks >= 60)
            {
                tipTicks = 0;
                string[] tips = IsEnglish ? gtaTipsEng : gtaTipsDe;
                tipIndex = (tipIndex + 1) % tips.Length;
            }

            if (loadProgress >= 100f)
            {
                loadProgress = 100f;
                if (currentAccount != null)
                    state = GameState.MainMenu;
                else
                {
                    authFocusField = 0;
                    state = GameState.Auth;
                }
                SyncCombatCursorVisibility();
                UpdateLobbyMusicState();
                SyncDiscordPresence(force: true);
            }
        }

        private void UpdatePlaying()
        {
            const float dt = 0.02f;
            runElapsedSeconds += dt;
            waterWavePhase += 0.08f;

            foreach (GrassTuft gt in grassTufts)
                gt.SwayPhase += 0.05f;
            foreach (MapProp prop in mapProps)
                prop.SwayPhase += prop.Kind == PropKind.Tree ? 0.025f : 0.055f;

            if (hurtSoundCooldown > 0) hurtSoundCooldown--;
            if (terrainParticleTimer > 0) terrainParticleTimer--;
            if (mapBannerTicks > 0) mapBannerTicks--;
            if (dashCooldown > 0) dashCooldown--;
            if (damageFlashAlpha > 0f)
                damageFlashAlpha = Math.Max(0f, damageFlashAlpha - 0.028f);
            if (gunRecoil > 0f)
                gunRecoil = Math.Max(0f, gunRecoil - 1.2f);
            if (screenShakeMag > 0f)
                screenShakeMag = Math.Max(0f, screenShakeMag - 0.85f);

            if (reloadTimer > 0)
            {
                reloadTimer--;
                if (reloadTimer == 0)
                    currentAmmo = GetWeaponMagCapacity(equippedWeapon);
            }

            if (runElapsedSeconds >= nextFiveMinuteIndex * 300f && mapTransitionProgress == 0f)
            {
                nextFiveMinuteIndex++;
                SwitchToNextMap(T("5-Minuten-Übergang", "5-Minute Transition"), withTransition: true);
            }

            if (mapTransitionProgress > 0f)
            {
                mapTransitionProgress += 0.03f;
                if (!mapTransitionSwapped && mapTransitionProgress >= 0.5f)
                {
                    mapTransitionSwapped = true;
                    int nextIdx = (((int)currentMap) + 1) % Enum.GetValues<MapType>().Length;
                    GenerateMapEnvironment((MapType)nextIdx);
                    bloodSplatters.Clear();
                    terrainPixels.Clear();
                    shellCasings.Clear();
                    sparkParticles.Clear();
                }

                if (mapTransitionProgress >= 1.0f)
                {
                    mapTransitionProgress = 0f;
                    mapTransitionSwapped = false;
                }
            }

            float dx = 0, dy = 0;
            bool shiftHeld = IsKeyPressed(Keys.ShiftKey) || IsKeyPressed(Keys.LShiftKey) || IsKeyPressed(Keys.RShiftKey);
            bool spaceHeld = IsKeyPressed(Keys.Space);

            if (this.ContainsFocus && (Control.ModifierKeys & Keys.Alt) == 0)
            {
                if (IsKeyPressed(Keys.W) || IsKeyPressed(Keys.Up)) dy -= 1;
                if (IsKeyPressed(Keys.S) || IsKeyPressed(Keys.Down)) dy += 1;
                if (IsKeyPressed(Keys.A) || IsKeyPressed(Keys.Left)) dx -= 1;
                if (IsKeyPressed(Keys.D) || IsKeyPressed(Keys.Right)) dx += 1;
                if (spaceHeld) TriggerDash(dx, dy);
            }

            if (dashTimer > 0)
            {
                dashTimer--;
                playerX += dashDirX * 14.5f;
                playerY += dashDirY * 14.5f;
                float half = PlayerSize / 2f;
                playerX = Math.Clamp(playerX, half, this.ClientSize.Width - half);
                playerY = Math.Clamp(playerY, half, this.ClientSize.Height - half);
                SpawnSparks(playerX, playerY, Color.Aqua, highParticles ? 3 : 1);
                isMovingNow = true;
                isSprintingNow = true;
            }
            else
            {
                isMovingNow = (dx != 0 || dy != 0);
                isSwimmingNow = IsInRiver(playerX, playerY);

                if (isMovingNow && shiftHeld && !isExhausted && stamina > 1f)
                {
                    isSprintingNow = true;
                    stamina = Math.Max(0f, stamina - 1.35f);
                    if (stamina <= 0.1f)
                        isExhausted = true;
                }
                else
                {
                    isSprintingNow = false;
                    float regenBoost = 1f + (staminaRegenLevel - 1) * 0.25f;
                    float regen = (isMovingNow ? 0.35f : 0.85f) * regenBoost;
                    stamina = Math.Min(MaxStamina, stamina + regen);
                    if (isExhausted && stamina >= 30f)
                        isExhausted = false;
                }

                if (isMovingNow)
                {
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    float baseSpd = GetPlayerSpeed();
                    float sprintMult = isSprintingNow ? 1.52f : 1.0f;
                    float waterMult = isSwimmingNow ? (isSprintingNow ? 0.78f : 0.68f) : 1.0f;
                    float finalSpd = baseSpd * sprintMult * waterMult;

                    playerX += (dx / len) * finalSpd;
                    playerY += (dy / len) * finalSpd;

                    float half = PlayerSize / 2f;
                    playerX = Math.Clamp(playerX, half, this.ClientSize.Width - half);
                    playerY = Math.Clamp(playerY, half, this.ClientSize.Height - half);

                    walkAnimPhase += isSprintingNow ? 0.44f : (isSwimmingNow ? 0.24f : 0.28f);

                    if (isSwimmingNow)
                        audioMixer?.SetMovementLoop(swimClip, gain: isSprintingNow ? 0.65f : 0.50f, speedFactor: isSprintingNow ? 1.25f : 1.0f);
                    else
                        audioMixer?.SetMovementLoop(grassRunClip, gain: isSprintingNow ? 0.65f : 0.50f, speedFactor: isSprintingNow ? 1.30f : 1.0f);

                    if (terrainParticleTimer == 0)
                    {
                        int pCount = isSprintingNow ? (highParticles ? 4 : 2) : (highParticles ? 2 : 1);
                        if (isSwimmingNow)
                        {
                            SpawnTerrainPixels(playerX, playerY + 12, isWater: true, count: pCount);
                            terrainParticleTimer = isSprintingNow ? 8 : 12;
                        }
                        else
                        {
                            SpawnTerrainPixels(playerX, playerY + 14, isWater: false, count: pCount);
                            terrainParticleTimer = isSprintingNow ? 6 : 10;
                        }
                    }
                }
                else
                {
                    isSprintingNow = false;
                    walkAnimPhase *= 0.75f;
                    audioMixer?.StopMovementLoop();
                }
            }

            UpdateCoOpMates();

            float aimDx = mousePos.X - playerX;
            float aimDy = mousePos.Y - playerY;
            float aimDist = MathF.Sqrt(aimDx * aimDx + aimDy * aimDy);
            if (aimDist > 1f)
                playerAngle = MathF.Atan2(aimDy, aimDx);

            if (shootCooldown > 0) shootCooldown--;
            if (mouseDown && shootCooldown == 0 && reloadTimer == 0 && aimDist > 1f)
            {
                if (currentAmmo <= 0)
                {
                    if (autoReload) StartReload();
                }
                else
                {
                    currentAmmo--;
                    float baseAngle = MathF.Atan2(aimDy, aimDx);
                    int dmg = GetWeaponDamage(equippedWeapon);
                    float muzzleX = playerX + MathF.Cos(baseAngle) * 22f;
                    float muzzleY = playerY + MathF.Sin(baseAngle) * 22f;

                    muzzleFlashes.Add(new MuzzleFlash
                    {
                        X = muzzleX,
                        Y = muzzleY,
                        Angle = baseAngle,
                        Life = 3,
                        MaxLife = 3,
                        Radius = equippedWeapon == 2 ? 16f : 11f
                    });

                    SpawnSparks(muzzleX, muzzleY, Color.Gold, highParticles ? 5 : 2);
                    AddShake(equippedWeapon == 2 ? 6.0f : 2.2f);

                    float ejectAngle = baseAngle + MathF.PI * 0.55f + ((float)rng.NextDouble() - 0.5f) * 0.35f;
                    float ejectSpd = 1.6f + (float)rng.NextDouble() * 1.8f;
                    shellCasings.Add(new ShellCasing
                    {
                        X = playerX + MathF.Cos(baseAngle) * 6f,
                        Y = playerY + MathF.Sin(baseAngle) * 6f,
                        Vx = MathF.Cos(ejectAngle) * ejectSpd,
                        Vy = MathF.Sin(ejectAngle) * ejectSpd,
                        Angle = baseAngle,
                        Spin = ((float)rng.NextDouble() - 0.5f) * 0.45f,
                        Life = 45
                    });

                    gunRecoil = equippedWeapon == 2 ? 7.5f : 4.5f;

                    if (equippedWeapon == 2)
                    {
                        float[] spreads = { -0.14f, 0f, 0.14f };
                        foreach (float sp in spreads)
                        {
                            float a = baseAngle + sp;
                            bullets.Add(new Bullet
                            {
                                X = muzzleX,
                                Y = muzzleY,
                                Vx = MathF.Cos(a) * 13.5f,
                                Vy = MathF.Sin(a) * 13.5f,
                                Damage = dmg,
                                Color = Color.Orange
                            });
                        }
                    }
                    else
                    {
                        float speed = equippedWeapon == 3 ? 17f : 14f;
                        Color bCol = equippedWeapon == 3 ? Color.Cyan : Color.Gold;
                        bullets.Add(new Bullet
                        {
                            X = muzzleX,
                            Y = muzzleY,
                            Vx = MathF.Cos(baseAngle) * speed,
                            Vy = MathF.Sin(baseAngle) * speed,
                            Damage = dmg,
                            Color = bCol
                        });
                    }

                    audioMixer?.PlayOneShot(shootClip, gain: 0.85f);
                    shootCooldown = GetShootCooldownMax();

                    if (currentAmmo == 0 && autoReload)
                        StartReload();
                }
            }

            if (zombiesToSpawnInWave > 0)
            {
                if (spawnCooldown > 0)
                    spawnCooldown--;
                else
                {
                    SpawnZombieAtEdge();
                    zombiesToSpawnInWave--;
                    spawnCooldown = Math.Max(13, 46 - wave * 3);
                }
            }
            else if (zombies.Count == 0)
            {
                wave++;
                zombiesToSpawnInWave = 5 + wave * 3;
                int waveCoins = (int)MathF.Round((35 + wave * 12) * GetCoinMultiplier());
                coins += waveCoins;
                GrantBattlePassXp(60 + wave * 20);
                int heal = equippedGear == 2 ? 40 : 20;
                playerHp = Math.Min(maxPlayerHp, playerHp + heal);
                SaveGameData();
                audioMixer?.PlayOneShot(waveClip, gain: 0.75f);
                SwitchToNextMap(T($"Runde {wave}", $"Wave {wave}"), withTransition: false);
                SyncDiscordPresence(force: true);
            }

            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                Bullet b = bullets[i];
                b.X += b.Vx;
                b.Y += b.Vy;
                b.Life--;

                if (b.Life <= 0 ||
                    b.X < -20 || b.X > this.ClientSize.Width + 20 ||
                    b.Y < -20 || b.Y > this.ClientSize.Height + 20)
                {
                    bullets.RemoveAt(i);
                }
            }

            for (int i = muzzleFlashes.Count - 1; i >= 0; i--)
            {
                muzzleFlashes[i].Life--;
                if (muzzleFlashes[i].Life <= 0)
                    muzzleFlashes.RemoveAt(i);
            }

            for (int i = shellCasings.Count - 1; i >= 0; i--)
            {
                ShellCasing sc = shellCasings[i];
                sc.X += sc.Vx;
                sc.Y += sc.Vy;
                sc.Vx *= 0.86f;
                sc.Vy *= 0.86f;
                sc.Angle += sc.Spin;
                sc.Spin *= 0.88f;
                sc.Life--;
                if (sc.Life <= 0)
                    shellCasings.RemoveAt(i);
            }

            for (int i = sparkParticles.Count - 1; i >= 0; i--)
            {
                SparkParticle sp = sparkParticles[i];
                sp.X += sp.Vx;
                sp.Y += sp.Vy;
                sp.Vx *= 0.85f;
                sp.Vy *= 0.85f;
                sp.Life--;
                if (sp.Life <= 0)
                    sparkParticles.RemoveAt(i);
            }

            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                PickupItem pk = pickups[i];
                pk.BobPhase += 0.14f;
                pk.Life--;
                if (pk.Life <= 0)
                {
                    pickups.RemoveAt(i);
                    continue;
                }

                float pdx = playerX - pk.X;
                float pdy = playerY - pk.Y;
                if (pdx * pdx + pdy * pdy <= 32f * 32f)
                {
                    if (pk.Kind == PickupKind.Coin)
                    {
                        int gained = (int)MathF.Round(pk.Value * GetCoinMultiplier());
                        coins += gained;
                        score += gained * 10;
                        SpawnSparks(pk.X, pk.Y, Color.Gold, highParticles ? 8 : 4);
                    }
                    else
                    {
                        playerHp = Math.Min(maxPlayerHp, playerHp + pk.Value);
                        SpawnSparks(pk.X, pk.Y, Color.LimeGreen, highParticles ? 8 : 4);
                    }
                    audioMixer?.PlayOneShot(pickupClip, gain: 0.75f);
                    pickups.RemoveAt(i);
                }
            }

            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                Zombie z = zombies[i];
                if (z.HitFlashTicks > 0) z.HitFlashTicks--;

                float zdx = playerX - z.X;
                float zdy = playerY - z.Y;
                float zDist = MathF.Sqrt(zdx * zdx + zdy * zdy);

                if (zDist > 0.1f)
                {
                    float zWaterMod = IsInRiver(z.X, z.Y) ? 0.72f : 1.0f;
                    z.X += (zdx / zDist) * z.Speed * zWaterMod;
                    z.Y += (zdy / zDist) * z.Speed * zWaterMod;
                    z.Angle = MathF.Atan2(zdy, zdx);
                    z.WalkPhase += 0.22f * z.Speed;
                }

                bool zombieDied = false;
                for (int j = bullets.Count - 1; j >= 0; j--)
                {
                    Bullet b = bullets[j];
                    float hitDist = (z.Size / 2f) + 5f;
                    float distSq = (b.X - z.X) * (b.X - z.X) + (b.Y - z.Y) * (b.Y - z.Y);
                    if (distSq <= hitDist * hitDist)
                    {
                        float impactAngle = MathF.Atan2(b.Vy, b.Vx);
                        bullets.RemoveAt(j);
                        z.Hp -= b.Damage;
                        z.HitFlashTicks = 4;
                        audioMixer?.PlayOneShot(hitClip, gain: 0.55f);

                        SpawnBlood(b.X, b.Y, impactAngle, z.Hp <= 0 ? 22 : 10, z.Hp <= 0);
                        SpawnSparks(b.X, b.Y, Color.OrangeRed, highParticles ? 5 : 2);

                        if (z.Hp <= 0)
                        {
                            float zx = z.X;
                            float zy = z.Y;
                            zombies.RemoveAt(i);
                            zombieDied = true;
                            kills++;
                            score += 100 * wave;
                            coins += (int)MathF.Round(15 * GetCoinMultiplier());
                            GrantBattlePassXp(15);
                            AddShake(3.2f);

                            double dropRoll = rng.NextDouble();
                            if (dropRoll < 0.24)
                            {
                                pickups.Add(new PickupItem
                                {
                                    Kind = PickupKind.Coin,
                                    X = zx,
                                    Y = zy,
                                    Value = 15 + wave * 3,
                                    Life = 450,
                                    BobPhase = 0f
                                });
                            }
                            else if (dropRoll < 0.36)
                            {
                                pickups.Add(new PickupItem
                                {
                                    Kind = PickupKind.Medkit,
                                    X = zx,
                                    Y = zy,
                                    Value = 20,
                                    Life = 450,
                                    BobPhase = 0f
                                });
                            }
                        }
                        break;
                    }
                }

                if (zombieDied) continue;

                float touchDist = (z.Size / 2f) + (PlayerSize / 2f) - 4f;
                float pDistSq = (playerX - z.X) * (playerX - z.X) + (playerY - z.Y) * (playerY - z.Y);
                if (pDistSq <= touchDist * touchDist)
                {
                    zombies.RemoveAt(i);
                    if (dashTimer > 0)
                    {
                        SpawnSparks(playerX, playerY, Color.Cyan, highParticles ? 8 : 4);
                        continue;
                    }

                    int dmgTaken = Math.Max(4, (int)MathF.Round(18 * GetDamageReduction()));
                    playerHp -= dmgTaken;
                    damageFlashAlpha = Math.Min(0.78f, damageFlashAlpha + 0.52f);
                    AddShake(7.5f);

                    if (hurtSoundCooldown == 0)
                    {
                        audioMixer?.PlayOneShot(hurtClip, gain: 0.90f);
                        hurtSoundCooldown = 14;
                    }

                    SpawnBlood(playerX, playerY, z.Angle, 16, true);

                    if (playerHp <= 0)
                    {
                        playerHp = 0;
                        audioMixer?.StopMovementLoop();
                        RecordRunStats();
                        state = GameState.GameOver;
                        SyncCombatCursorVisibility();
                        SyncDiscordPresence(force: true);
                        return;
                    }
                }
            }

            for (int i = bloodParticles.Count - 1; i >= 0; i--)
            {
                BloodParticle p = bloodParticles[i];
                p.X += p.Vx;
                p.Y += p.Vy;
                p.Vx *= 0.88f;
                p.Vy *= 0.88f;
                p.Life--;
                if (p.Life <= 0) bloodParticles.RemoveAt(i);
            }

            for (int i = terrainPixels.Count - 1; i >= 0; i--)
            {
                TerrainPixel tp = terrainPixels[i];
                tp.X += tp.Vx;
                tp.Y += tp.Vy;
                tp.Vx *= 0.85f;
                tp.Vy *= 0.85f;
                tp.Life--;
                if (tp.Life <= 0) terrainPixels.RemoveAt(i);
            }
        }

        private void SpawnSparks(float x, float y, Color col, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2;
                float spd = 1.5f + (float)rng.NextDouble() * 4.5f;
                sparkParticles.Add(new SparkParticle
                {
                    X = x,
                    Y = y,
                    Vx = (float)Math.Cos(ang) * spd,
                    Vy = (float)Math.Sin(ang) * spd,
                    Life = rng.Next(8, 16),
                    Size = rng.Next(3, 6),
                    Color = col
                });
            }
        }

        private void SpawnTerrainPixels(float x, float y, bool isWater, int count)
        {
            Color[] grassColors =
            {
                Color.FromArgb(72, 165, 62),
                Color.FromArgb(42, 112, 44),
                Color.FromArgb(115, 88, 52)
            };
            Color[] waterColors =
            {
                Color.FromArgb(185, 232, 255),
                Color.FromArgb(95, 185, 240),
                Color.FromArgb(230, 245, 255)
            };

            for (int i = 0; i < count; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2;
                float spd = 0.8f + (float)rng.NextDouble() * 2.4f;
                Color col = isWater
                    ? waterColors[rng.Next(waterColors.Length)]
                    : grassColors[rng.Next(grassColors.Length)];

                terrainPixels.Add(new TerrainPixel
                {
                    X = x + rng.Next(-8, 9),
                    Y = y + rng.Next(-6, 7),
                    Vx = (float)Math.Cos(ang) * spd,
                    Vy = (float)Math.Sin(ang) * spd - 0.7f,
                    Life = rng.Next(10, 20),
                    Size = rng.Next(4, 7),
                    Color = col
                });
            }
        }

        private bool IsInRiver(float x, float y)
        {
            if (horizontalRiver)
                return MathF.Abs(y - GetRiverCenterAt(x)) < riverWidth * 0.5f;
            return MathF.Abs(x - GetRiverCenterAt(y)) < riverWidth * 0.5f;
        }

        private void SpawnZombieAtEdge()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            int edge = rng.Next(0, 4);

            float sx = 0, sy = 0;
            switch (edge)
            {
                case 0: sx = rng.Next(0, w); sy = -24; break;
                case 1: sx = w + 24; sy = rng.Next(0, h); break;
                case 2: sx = rng.Next(0, w); sy = h + 24; break;
                case 3: sx = -24; sy = rng.Next(0, h); break;
            }

            int hp = 2 + (wave / 2);
            float baseSpeed = 1.35f + Math.Min(2.4f, wave * 0.16f);
            zombies.Add(new Zombie
            {
                X = sx,
                Y = sy,
                Speed = baseSpeed + (float)(rng.NextDouble() * 0.6),
                WalkPhase = (float)(rng.NextDouble() * Math.PI * 2),
                Hp = hp,
                MaxHp = hp
            });
        }

        private void SpawnBlood(float x, float y, float dirAngle, int count, bool addSplatter)
        {
            if (!bloodEffects) return;

            int actualCount = highParticles ? count : Math.Max(2, count / 2);
            Color[] bloodShades =
            {
                Color.FromArgb(190, 22, 22),
                Color.FromArgb(145, 12, 12),
                Color.FromArgb(220, 35, 30)
            };

            for (int i = 0; i < actualCount; i++)
            {
                float spread = ((float)rng.NextDouble() - 0.5f) * 1.35f;
                float a = dirAngle + spread;
                float spd = 1.8f + (float)rng.NextDouble() * 5.5f;
                bloodParticles.Add(new BloodParticle
                {
                    X = x,
                    Y = y,
                    Vx = MathF.Cos(a) * spd,
                    Vy = MathF.Sin(a) * spd,
                    Life = rng.Next(15, 30),
                    Size = rng.Next(4, 10),
                    Color = bloodShades[rng.Next(bloodShades.Length)]
                });
            }

            if (addSplatter)
            {
                if (bloodSplatters.Count > 200)
                    bloodSplatters.RemoveAt(0);

                bloodSplatters.Add(new BloodSplatter
                {
                    X = x + rng.Next(-10, 11),
                    Y = y + rng.Next(-10, 11),
                    Size = rng.Next(10, 24),
                    Color = Color.FromArgb(125, 120, 12, 12)
                });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            switch (state)
            {
                case GameState.Loading:
                    DrawGtaStarLoadingScreen(g);
                    break;
                case GameState.Auth:
                    DrawAuthScreen(g);
                    break;
                case GameState.MainMenu:
                    DrawMainMenu(g);
                    break;
                case GameState.Tutorial:
                    DrawGameplay(g);
                    DrawTutorialOverlay(g);
                    break;
                case GameState.Playing:
                    DrawGameplay(g);
                    break;
                case GameState.Paused:
                    DrawGameplay(g);
                    DrawPauseOverlay(g);
                    break;
                case GameState.GameOver:
                    DrawGameplay(g);
                    DrawGameOverOverlay(g);
                    break;
            }
        }

        private void DrawGtaStarLoadingScreen(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            g.Clear(Color.FromArgb(6, 9, 20));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var aura1 = new SolidBrush(Color.FromArgb(34, 0, 210, 255)))
                g.FillEllipse(aura1, w * 0.12f - 160f + MathF.Sin(loadingFxPhase) * 24f, h * 0.18f - 160f, 320f, 320f);
            using (var aura2 = new SolidBrush(Color.FromArgb(36, 255, 75, 115)))
                g.FillEllipse(aura2, w * 0.82f - 180f + MathF.Cos(loadingFxPhase * 0.8f) * 28f, h * 0.20f - 180f, 360f, 360f);

            int horizonY = (int)(h * 0.66f);
            using var gridPen = new Pen(Color.FromArgb(55, 0, 205, 255), 1.4f);
            for (int gx = -w; gx <= w * 2; gx += 75)
            {
                float shift = (loadingFxPhase * 22f) % 75f;
                g.DrawLine(gridPen, gx + shift, horizonY, w * 0.5f + (gx + shift - w * 0.5f) * 2.1f, h);
            }
            for (int gy = horizonY; gy <= h; gy += 28)
            {
                float gShift = (loadingFxPhase * 14f) % 28f;
                float yPos = gy + gShift;
                if (yPos <= h)
                    g.DrawLine(gridPen, 0, yPos, w, yPos);
            }
            g.SmoothingMode = SmoothingMode.None;

            foreach (Star s in stars)
            {
                float pulse = (MathF.Sin(s.Phase) + 1f) * 0.5f;
                int alpha = Math.Clamp((int)(65 + pulse * 190), 65, 255);
                Color pCol = s.Size >= 4 ? Color.FromArgb(alpha, 120, 235, 255) : Color.FromArgb(alpha, 255, 240, 215);
                using var starBrush = new SolidBrush(pCol);
                g.FillRectangle(starBrush, s.X % Math.Max(1, w), s.Y % Math.Max(1, h - 90), s.Size, s.Size);
            }

            using (var sunGlow = new SolidBrush(Color.FromArgb(55, 255, 95, 65)))
                g.FillEllipse(sunGlow, w - 230, 50, 170, 170);
            using (var sunCore = new SolidBrush(Color.FromArgb(225, 255, 130, 80)))
                g.FillEllipse(sunCore, w - 195, 85, 100, 100);

            using (var titleFont = new Font("Impact", 50, FontStyle.Bold))
            using (var subFont = new Font("Consolas", 13, FontStyle.Bold))
            using (var pctFont = new Font("Consolas", 11, FontStyle.Bold))
            {
                g.DrawString("FLOS GAME", titleFont, Brushes.Black, 39, 34);
                g.DrawString("FLOS GAME", titleFont, Brushes.White, 35, 30);
                g.DrawString("REALISTIC PIXEL SURVIVAL  •  F11 FULLSCREEN", subFont, Brushes.Cyan, 37, 92);
                g.DrawString(T($"LADEN... {(int)loadProgress}%  (Klick / Leertaste = Skip)", $"LOADING... {(int)loadProgress}%  (Click / Space = Skip)"),
                    pctFont, Brushes.LightGray, 35, h - 108);
            }

            string[] tips = IsEnglish ? gtaTipsEng : gtaTipsDe;
            using (var tipFont = new Font("Consolas", 11, FontStyle.Regular))
                g.DrawString(tips[tipIndex], tipFont, Brushes.WhiteSmoke, 35, h - 80);

            int barX = 35;
            int barY = h - 46;
            int barW = Math.Max(100, w - 130);
            int barH = 12;
            int fillW = (int)(barW * (loadProgress / 100f));

            using (var bgBar = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
            using (var fgBar = new SolidBrush(Color.FromArgb(255, 55, 210, 255)))
            using (var sheenBar = new SolidBrush(Color.FromArgb(130, 255, 255, 255)))
            {
                g.FillRectangle(bgBar, barX, barY, barW, barH);
                g.FillRectangle(fgBar, barX, barY, fillW, barH);
                int sheenX = barX + (int)((loadingFxPhase * 90f) % Math.Max(1, fillW));
                if (fillW > 18)
                    g.FillRectangle(sheenBar, sheenX, barY, Math.Min(18, barX + fillW - sheenX), barH);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = w - 52f;
            float cy = h - 40f;
            float r = 15f;

            using var trackPen = new Pen(Color.FromArgb(85, 255, 255, 255), 3.5f);
            using var spinPen = new Pen(Color.Cyan, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(trackPen, cx - r, cy - r, r * 2, r * 2);
            g.DrawArc(spinPen, cx - r, cy - r, r * 2, r * 2, spinnerAngle, 105f);
            g.SmoothingMode = SmoothingMode.None;
        }

        private void DrawAuthScreen(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            g.Clear(Color.FromArgb(12, 15, 26));

            foreach (Star s in stars)
            {
                int alpha = Math.Clamp((int)(40 + ((MathF.Sin(s.Phase) + 1f) * 0.5f) * 130), 35, 175);
                using var starBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                g.FillRectangle(starBrush, s.X % Math.Max(1, w), s.Y % Math.Max(1, h), s.Size, s.Size);
            }

            using var titleFont = new Font("Impact", 34, FontStyle.Bold);
            using var subFont = new Font("Consolas", 11, FontStyle.Bold);
            using var labelFont = new Font("Consolas", 10, FontStyle.Bold);
            using var inputFont = new Font("Consolas", 14, FontStyle.Bold);
            using var btnFont = new Font("Impact", 18, FontStyle.Bold);

            int cardX = authModeLoginRect.X;
            int cardY = authModeLoginRect.Y - 76;

            g.DrawString("FLOS GAME • ACCOUNT", titleFont, Brushes.White, cardX, cardY);
            string intro = accountsDb.Accounts.Count == 0
                ? T("Erster Start: Registriere zuerst deinen Spieler-Account.", "First launch: Register your player account first.")
                : T("Bitte einloggen oder einen neuen Account registrieren.", "Please log in or register a new account.");
            g.DrawString(intro, subFont, Brushes.Cyan, cardX, cardY + 44);

            using (var loginBg = new SolidBrush(authMode == AuthMode.Login ? Color.FromArgb(45, 130, 230) : Color.FromArgb(26, 30, 46)))
            using (var regBg = new SolidBrush(authMode == AuthMode.Register ? Color.FromArgb(215, 95, 30) : Color.FromArgb(26, 30, 46)))
            {
                g.FillRectangle(loginBg, authModeLoginRect);
                g.DrawRectangle(authMode == AuthMode.Login ? Pens.White : Pens.SlateGray, authModeLoginRect);
                SizeF lSz = g.MeasureString("LOGIN", labelFont);
                g.DrawString("LOGIN", labelFont, Brushes.White,
                    authModeLoginRect.X + (authModeLoginRect.Width - lSz.Width) / 2f,
                    authModeLoginRect.Y + (authModeLoginRect.Height - lSz.Height) / 2f);

                string regTxt = T("REGISTRIEREN", "REGISTER");
                g.FillRectangle(regBg, authModeRegisterRect);
                g.DrawRectangle(authMode == AuthMode.Register ? Pens.White : Pens.SlateGray, authModeRegisterRect);
                SizeF rSz = g.MeasureString(regTxt, labelFont);
                g.DrawString(regTxt, labelFont, Brushes.White,
                    authModeRegisterRect.X + (authModeRegisterRect.Width - rSz.Width) / 2f,
                    authModeRegisterRect.Y + (authModeRegisterRect.Height - rSz.Height) / 2f);
            }

            g.DrawString(T("BENUTZERNAME (min. 3 Zeichen):", "USERNAME (min. 3 chars):"), labelFont, Brushes.Silver, authUserBoxRect.X, authUserBoxRect.Y - 22);
            using (var uBg = new SolidBrush(Color.FromArgb(24, 28, 44)))
            using (var uPen = new Pen(authFocusField == 0 ? Color.Cyan : Color.SlateGray, 2.5f))
            {
                g.FillRectangle(uBg, authUserBoxRect);
                g.DrawRectangle(uPen, authUserBoxRect);
            }
            string uDisp = authUsername + (authFocusField == 0 ? "_" : "");
            g.DrawString(uDisp, inputFont, Brushes.White, authUserBoxRect.X + 10, authUserBoxRect.Y + 9);

            g.DrawString(T("PASSWORT (min. 4 Zeichen):", "PASSWORD (min. 4 chars):"), labelFont, Brushes.Silver, authPassBoxRect.X, authPassBoxRect.Y - 22);
            using (var pBg = new SolidBrush(Color.FromArgb(24, 28, 44)))
            using (var pPen = new Pen(authFocusField == 1 ? Color.Cyan : Color.SlateGray, 2.5f))
            {
                g.FillRectangle(pBg, authPassBoxRect);
                g.DrawRectangle(pPen, authPassBoxRect);
            }
            string maskedPass = new string('*', authPassword.Length) + (authFocusField == 1 ? "_" : "");
            g.DrawString(maskedPass, inputFont, Brushes.White, authPassBoxRect.X + 10, authPassBoxRect.Y + 9);

            using (var subBg = new SolidBrush(Color.FromArgb(38, 165, 85)))
            using (var subPen = new Pen(Color.White, 2.5f))
            {
                g.FillRectangle(subBg, authSubmitRect);
                g.DrawRectangle(subPen, authSubmitRect);
            }
            string submitLabel = authMode == AuthMode.Register
                ? T("ACCOUNT ERSTELLEN", "CREATE ACCOUNT")
                : T("EINLOGGEN", "LOG IN");
            SizeF sSz = g.MeasureString(submitLabel, btnFont);
            g.DrawString(submitLabel, btnFont, Brushes.White,
                authSubmitRect.X + (authSubmitRect.Width - sSz.Width) / 2f,
                authSubmitRect.Y + (authSubmitRect.Height - sSz.Height) / 2f - 1);

            if (!string.IsNullOrEmpty(authMessage))
            {
                Brush msgBrush = authMessageIsError ? Brushes.OrangeRed : Brushes.LimeGreen;
                g.DrawString(authMessage, labelFont, msgBrush, cardX, authSubmitRect.Bottom + 10);
            }
        }

        private void DrawMainMenu(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            g.Clear(Color.FromArgb(14, 17, 29));

            foreach (Star s in stars)
            {
                int alpha = Math.Clamp((int)(45 + ((MathF.Sin(s.Phase) + 1f) * 0.5f) * 140), 40, 190);
                using var starBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                g.FillRectangle(starBrush, s.X % Math.Max(1, w), s.Y % Math.Max(1, h), s.Size, s.Size);
            }

            using var titleFont = new Font("Impact", 36, FontStyle.Bold);
            using var coinFont = new Font("Consolas", 13, FontStyle.Bold);
            using var tabFont = new Font("Consolas", 8.5f, FontStyle.Bold);
            using var secFont = new Font("Consolas", 14, FontStyle.Bold);
            using var labelFont = new Font("Consolas", 10, FontStyle.Regular);
            using var inputFont = new Font("Consolas", 16, FontStyle.Bold);
            using var btnFont = new Font("Impact", 20, FontStyle.Bold);

            g.DrawString("FLOS GAME", titleFont, Brushes.Black, 32, 22);
            g.DrawString("FLOS GAME", titleFont, Brushes.White, 30, 20);

            using (var outBg = new SolidBrush(Color.FromArgb(70, 32, 36)))
            using (var outPen = new Pen(Color.IndianRed, 2f))
            {
                g.FillRectangle(outBg, logoutBtnRect);
                g.DrawRectangle(outPen, logoutBtnRect);
                SizeF oSz = g.MeasureString("LOGOUT", tabFont);
                g.DrawString("LOGOUT", tabFont, Brushes.White,
                    logoutBtnRect.X + (logoutBtnRect.Width - oSz.Width) / 2f,
                    logoutBtnRect.Y + (logoutBtnRect.Height - oSz.Height) / 2f);
            }

            using var coinBadge = new SolidBrush(Color.FromArgb(32, 36, 54));
            using var coinPen = new Pen(Color.Gold, 2f);
            Rectangle coinBox = new Rectangle(w - 205, 22, 175, 42);
            g.FillRectangle(coinBadge, coinBox);
            g.DrawRectangle(coinPen, coinBox);
            g.DrawString($"COINS: {coins}", coinFont, Brushes.Gold, coinBox.X + 14, coinBox.Y + 11);

            string[] tabLabels =
            {
                T("HAUPT", "MAIN"),
                "SKIN",
                T("WAFFEN", "ARMORY"),
                T("KARRIERE", "STATS"),
                "SHOP",
                "SPIND",
                "SPIN",
                "PASS",
                T("CONFIG", "CONFIG")
            };
            MainTab[] tabEnums =
            {
                MainTab.Haupt,
                MainTab.Skin,
                MainTab.Waffenkammer,
                MainTab.Karriere,
                MainTab.Shop,
                MainTab.Spind,
                MainTab.Spin,
                MainTab.BattlePass,
                MainTab.Einstellung
            };

            for (int i = 0; i < 9; i++)
            {
                bool selected = activeTab == tabEnums[i];
                Color baseCol = tabEnums[i] switch
                {
                    MainTab.Shop => Color.FromArgb(215, 95, 30),
                    MainTab.Spind => Color.FromArgb(52, 115, 195),
                    MainTab.Spin => Color.FromArgb(155, 65, 210),
                    MainTab.BattlePass => Color.FromArgb(205, 155, 25),
                    MainTab.Einstellung => Color.FromArgb(0, 150, 136),
                    _ => Color.FromArgb(45, 130, 230)
                };
                Color bgCol = selected ? baseCol : Color.FromArgb(26, 30, 46);

                using var bg = new SolidBrush(bgCol);
                using var border = new Pen(selected ? Color.White : Color.SlateGray, selected ? 2.5f : 1.5f);
                g.FillRectangle(bg, mainTabRects[i]);
                g.DrawRectangle(border, mainTabRects[i]);

                SizeF sz = g.MeasureString(tabLabels[i], tabFont);
                g.DrawString(tabLabels[i], tabFont, Brushes.White,
                    mainTabRects[i].X + (mainTabRects[i].Width - sz.Width) / 2f,
                    mainTabRects[i].Y + (mainTabRects[i].Height - sz.Height) / 2f);
            }

            switch (activeTab)
            {
                case MainTab.Haupt:
                    DrawHauptTab(g, secFont, labelFont, inputFont, btnFont);
                    break;
                case MainTab.Skin:
                    DrawSkinTab(g, secFont, tabFont, labelFont);
                    break;
                case MainTab.Waffenkammer:
                    DrawWaffenkammerTab(g, secFont, tabFont, labelFont);
                    break;
                case MainTab.Karriere:
                    DrawKarriereTab(g, secFont, coinFont, labelFont);
                    break;
                case MainTab.Shop:
                    DrawShopTab(g, secFont, tabFont, labelFont);
                    break;
                case MainTab.Spind:
                    DrawSpindTab(g, secFont, tabFont, labelFont);
                    break;
                case MainTab.Spin:
                    DrawSpinTab(g, secFont, tabFont, labelFont, btnFont);
                    break;
                case MainTab.BattlePass:
                    DrawBattlePassTab(g, secFont, tabFont, labelFont);
                    break;
                case MainTab.Einstellung:
                    DrawEinstellungTab(g, secFont, tabFont, labelFont);
                    break;
            }

            if (mpView != MultiplayerView.None)
                DrawMultiplayerModal(g, secFont, labelFont, inputFont, btnFont);
        }

        private void DrawMultiplayerModal(Graphics g, Font secFont, Font labelFont, Font inputFont, Font btnFont)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            using var dim = new SolidBrush(Color.FromArgb(185, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, w, h);

            int mpW = Math.Min(720, w - 60);
            int mpH = Math.Min(430, h - 130);
            int mpX = (w - mpW) / 2;
            int mpY = Math.Max(80, (h - mpH) / 2);
            Rectangle modal = new Rectangle(mpX, mpY, mpW, mpH);

            using var bg = new SolidBrush(Color.FromArgb(20, 26, 42));
            using var border = new Pen(Color.Cyan, 2.5f);
            g.FillRectangle(bg, modal);
            g.DrawRectangle(border, modal);

            using var closeBg = new SolidBrush(Color.FromArgb(150, 35, 40));
            g.FillRectangle(closeBg, mpCloseBtnRect);
            g.DrawRectangle(Pens.White, mpCloseBtnRect);
            g.DrawString("X", labelFont, Brushes.White, mpCloseBtnRect.X + 10, mpCloseBtnRect.Y + 8);

            if (mpView == MultiplayerView.ModeSelect)
            {
                g.DrawString(T("MULTIPLAYER • MODUS WÄHLEN", "MULTIPLAYER • CHOOSE MODE"), secFont, Brushes.Cyan, mpX + 40, mpY + 24);
                g.DrawString(T("Erstelle eine neue Lobby mit Server-Code oder tritt per Code bei.", "Create a new lobby with a server code or join using a code."),
                    labelFont, Brushes.Silver, mpX + 40, mpY + 60);

                using var createBg = new SolidBrush(Color.FromArgb(38, 165, 85));
                g.FillRectangle(createBg, mpCreateLobbyBtnRect);
                g.DrawRectangle(Pens.White, mpCreateLobbyBtnRect);
                string cTxt = T("LOBBY ERSTELLEN", "CREATE LOBBY");
                SizeF cSz = g.MeasureString(cTxt, btnFont);
                g.DrawString(cTxt, btnFont, Brushes.White,
                    mpCreateLobbyBtnRect.X + (mpCreateLobbyBtnRect.Width - cSz.Width) / 2f,
                    mpCreateLobbyBtnRect.Y + (mpCreateLobbyBtnRect.Height - cSz.Height) / 2f - 1);

                using var joinBg = new SolidBrush(Color.FromArgb(45, 130, 230));
                g.FillRectangle(joinBg, mpJoinByCodeBtnRect);
                g.DrawRectangle(Pens.White, mpJoinByCodeBtnRect);
                string jTxt = T("MIT CODE JOINEN", "JOIN WITH CODE");
                SizeF jSz = g.MeasureString(jTxt, btnFont);
                g.DrawString(jTxt, btnFont, Brushes.White,
                    mpJoinByCodeBtnRect.X + (mpJoinByCodeBtnRect.Width - jSz.Width) / 2f,
                    mpJoinByCodeBtnRect.Y + (mpJoinByCodeBtnRect.Height - jSz.Height) / 2f - 1);
            }
            else if (mpView == MultiplayerView.JoinByCode)
            {
                g.DrawString(T("MIT CODE JOINEN", "JOIN WITH CODE"), secFont, Brushes.Cyan, mpX + 40, mpY + 24);
                g.DrawString(T("Server-Code eingeben (z.B. A7K9M2):", "Enter Server Code (e.g. A7K9M2):"), labelFont, Brushes.Silver, mpX + 40, mpCodeInputRect.Y - 26);

                using var boxBg = new SolidBrush(Color.FromArgb(14, 18, 30));
                g.FillRectangle(boxBg, mpCodeInputRect);
                g.DrawRectangle(Pens.Cyan, mpCodeInputRect);
                g.DrawString(joinCodeInput + "_", inputFont, Brushes.Gold, mpCodeInputRect.X + 12, mpCodeInputRect.Y + 10);

                using var okBg = new SolidBrush(Color.FromArgb(38, 165, 85));
                g.FillRectangle(okBg, mpConfirmJoinBtnRect);
                g.DrawRectangle(Pens.White, mpConfirmJoinBtnRect);
                string okTxt = T("BEITRETEN", "JOIN LOBBY");
                SizeF okSz = g.MeasureString(okTxt, btnFont);
                g.DrawString(okTxt, btnFont, Brushes.White,
                    mpConfirmJoinBtnRect.X + (mpConfirmJoinBtnRect.Width - okSz.Width) / 2f,
                    mpConfirmJoinBtnRect.Y + (mpConfirmJoinBtnRect.Height - okSz.Height) / 2f - 1);

                using var backBg = new SolidBrush(Color.FromArgb(55, 62, 82));
                g.FillRectangle(backBg, mpBackToSelectBtnRect);
                g.DrawRectangle(Pens.White, mpBackToSelectBtnRect);
                string bTxt = T("ZURÜCK", "BACK");
                SizeF bSz = g.MeasureString(bTxt, btnFont);
                g.DrawString(bTxt, btnFont, Brushes.White,
                    mpBackToSelectBtnRect.X + (mpBackToSelectBtnRect.Width - bSz.Width) / 2f,
                    mpBackToSelectBtnRect.Y + (mpBackToSelectBtnRect.Height - bSz.Height) / 2f - 1);

                if (!string.IsNullOrEmpty(mpStatusMessage))
                    g.DrawString(mpStatusMessage, labelFont, mpStatusIsError ? Brushes.OrangeRed : Brushes.LimeGreen, mpX + 40, mpConfirmJoinBtnRect.Bottom + 14);
            }
            else if (mpView == MultiplayerView.LobbyRoom)
            {
                g.DrawString(T("LOBBY-RAUM", "LOBBY ROOM"), secFont, Brushes.Cyan, mpX + 24, mpY + 20);

                int leftX = mpX + 24;
                int leftW = mpCopyCodeBtnRect.X - leftX - 18;
                Rectangle leftBox = new Rectangle(leftX, mpY + 56, leftW, mpH - 80);
                using var leftBg = new SolidBrush(Color.FromArgb(15, 20, 34));
                g.FillRectangle(leftBg, leftBox);
                g.DrawRectangle(Pens.SlateGray, leftBox);

                g.DrawString(T($"SPIELER IN LOBBY ({lobbyPlayers.Count}/4):", $"PLAYERS IN LOBBY ({lobbyPlayers.Count}/4):"),
                    labelFont, Brushes.Gold, leftBox.X + 12, leftBox.Y + 10);

                for (int i = 0; i < lobbyPlayers.Count; i++)
                {
                    LobbyPlayer lp = lobbyPlayers[i];
                    Rectangle row = new Rectangle(leftBox.X + 12, leftBox.Y + 38 + i * 62, leftBox.Width - 24, 52);
                    using var rowBg = new SolidBrush(Color.FromArgb(26, 34, 54));
                    g.FillRectangle(rowBg, row);
                    g.DrawRectangle(lp.IsHost ? Pens.Gold : Pens.SlateGray, row);

                    int sIdx = Math.Clamp(lp.SkinIndex, 0, humanRigs.Length - 1);
                    if (humanRigs[sIdx]?.FullPreview != null)
                        g.DrawImage(humanRigs[sIdx]!.FullPreview!, row.X + 8, row.Y + 8, 36, 36);

                    string roleBadge = lp.IsHost ? " [HOST]" : "";
                    g.DrawString(lp.Name + roleBadge, labelFont, Brushes.White, row.X + 52, row.Y + 8);
                    g.DrawString($"{T("Skin", "Skin")}: {GetSkinName(sIdx)}  •  {(lp.IsReady ? T("BEREIT", "READY") : T("WARTE", "WAIT"))}",
                        labelFont, Brushes.LimeGreen, row.X + 52, row.Y + 28);
                }

                int rightX = mpCopyCodeBtnRect.X;
                int rightW = mpCopyCodeBtnRect.Width;
                g.DrawString(T("SERVER-CODE:", "SERVER CODE:"), labelFont, Brushes.Silver, rightX, mpY + 62);

                Rectangle codeCard = new Rectangle(rightX, mpY + 86, rightW, 68);
                using var codeBg = new SolidBrush(Color.FromArgb(14, 18, 30));
                g.FillRectangle(codeBg, codeCard);
                g.DrawRectangle(Pens.Gold, codeCard);
                g.DrawString(currentRoomCode, btnFont, Brushes.Gold, codeCard.X + 14, codeCard.Y + 18);

                using var copyBg = new SolidBrush(Color.FromArgb(45, 130, 230));
                g.FillRectangle(copyBg, mpCopyCodeBtnRect);
                g.DrawRectangle(Pens.White, mpCopyCodeBtnRect);
                string cpTxt = T("CODE KOPIEREN", "COPY CODE");
                SizeF cpSz = g.MeasureString(cpTxt, labelFont);
                g.DrawString(cpTxt, labelFont, Brushes.White,
                    mpCopyCodeBtnRect.X + (mpCopyCodeBtnRect.Width - cpSz.Width) / 2f,
                    mpCopyCodeBtnRect.Y + (mpCopyCodeBtnRect.Height - cpSz.Height) / 2f);

                using var startMpBg = new SolidBrush(Color.FromArgb(38, 165, 85));
                g.FillRectangle(startMpBg, mpStartMatchBtnRect);
                g.DrawRectangle(Pens.White, mpStartMatchBtnRect);
                string stTxt = "START";
                SizeF stSz = g.MeasureString(stTxt, btnFont);
                g.DrawString(stTxt, btnFont, Brushes.White,
                    mpStartMatchBtnRect.X + (mpStartMatchBtnRect.Width - stSz.Width) / 2f,
                    mpStartMatchBtnRect.Y + (mpStartMatchBtnRect.Height - stSz.Height) / 2f - 1);

                using var leaveBg = new SolidBrush(Color.FromArgb(95, 35, 40));
                g.FillRectangle(leaveBg, mpLeaveLobbyBtnRect);
                g.DrawRectangle(Pens.White, mpLeaveLobbyBtnRect);
                string lvTxt = T("LOBBY VERLASSEN", "LEAVE LOBBY");
                SizeF lvSz = g.MeasureString(lvTxt, labelFont);
                g.DrawString(lvTxt, labelFont, Brushes.White,
                    mpLeaveLobbyBtnRect.X + (mpLeaveLobbyBtnRect.Width - lvSz.Width) / 2f,
                    mpLeaveLobbyBtnRect.Y + (mpLeaveLobbyBtnRect.Height - lvSz.Height) / 2f);

                if (!string.IsNullOrEmpty(mpStatusMessage))
                    g.DrawString(mpStatusMessage, labelFont, mpStatusIsError ? Brushes.OrangeRed : Brushes.LimeGreen, rightX, mpLeaveLobbyBtnRect.Bottom + 10);
            }
        }

        private void DrawSpindTab(Graphics g, Font secFont, Font btnFont, Font labelFont)
        {
            g.DrawString(T("SPIND • WAFFEN, SKINS, LOBBYMUSIK & EXTRAS", "SPIND • WEAPONS, SKINS, LOBBY MUSIC & EXTRAS"),
                secFont, Brushes.DeepSkyBlue, 30, 114);

            SpindLockerTab[] tabs = { SpindLockerTab.Waffen, SpindLockerTab.Skins, SpindLockerTab.Lobbymusik, SpindLockerTab.Extras };
            string[] labels = { T("WAFFEN", "WEAPONS"), "SKINS", T("LOBBYMUSIK", "LOBBY MUSIC"), "EXTRAS" };

            for (int i = 0; i < 4; i++)
            {
                bool sel = activeSpindTab == tabs[i];
                using var bg = new SolidBrush(sel ? Color.FromArgb(52, 115, 195) : Color.FromArgb(26, 30, 46));
                g.FillRectangle(bg, spindLockerTabRects[i]);
                g.DrawRectangle(sel ? Pens.White : Pens.SlateGray, spindLockerTabRects[i]);
                SizeF sz = g.MeasureString(labels[i], btnFont);
                g.DrawString(labels[i], btnFont, Brushes.White,
                    spindLockerTabRects[i].X + (spindLockerTabRects[i].Width - sz.Width) / 2f,
                    spindLockerTabRects[i].Y + (spindLockerTabRects[i].Height - sz.Height) / 2f);
            }

            if (activeSpindTab == SpindLockerTab.Waffen)
            {
                BuildCardsGrid(weaponNamesDe.Length, 188, 140, 2);
                for (int i = 0; i < weaponNamesDe.Length; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool eq = equippedWeapon == i;
                    bool own = ownedWeapons[i];
                    using var bg = new SolidBrush(Color.FromArgb(24, 30, 48));
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(eq ? Pens.LimeGreen : Pens.SlateGray, r);
                    g.DrawString($"{GetWeaponName(i)} (Lv.{weaponLevels[i]})", btnFont, Brushes.White, r.X + 12, r.Y + 12);
                    g.DrawString($"{T("Schaden", "Dmg")}: {GetWeaponDamage(i)} | {T("Mag", "Mag")}: {GetWeaponMagCapacity(i)}", labelFont, Brushes.Gold, r.X + 12, r.Y + 40);
                    string st = eq ? T("AUSGERÜSTET", "EQUIPPED") : (own ? T("Klicken = Ausrüsten", "Click = Equip") : T($"Kaufen: {weaponPrices[i]} C", $"Buy: {weaponPrices[i]} C"));
                    g.DrawString(st, btnFont, own ? Brushes.LimeGreen : Brushes.OrangeRed, r.X + 12, r.Y + 86);
                }
            }
            else if (activeSpindTab == SpindLockerTab.Skins)
            {
                BuildCardsGrid(skinNamesDe.Length, 188, 135, 3);
                for (int i = 0; i < skinNamesDe.Length; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool eq = equippedSkin == i;
                    bool own = ownedSkins[i];
                    using var bg = new SolidBrush(Color.FromArgb(24, 30, 48));
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(eq ? Pens.LimeGreen : Pens.SlateGray, r);
                    if (humanRigs[i]?.FullPreview != null)
                        g.DrawImage(humanRigs[i]!.FullPreview!, r.X + 10, r.Y + 16, 48, 48);
                    g.DrawString(GetSkinName(i), btnFont, Brushes.White, r.X + 66, r.Y + 18);
                    string st = eq ? T("AKTIV", "ACTIVE") : (own ? T("Klicken = Ausrüsten", "Click = Equip") : T($"Kaufen: {skinPrices[i]} C", $"Buy: {skinPrices[i]} C"));
                    g.DrawString(st, labelFont, own ? Brushes.LimeGreen : Brushes.Gold, r.X + 66, r.Y + 48);
                }
            }
            else if (activeSpindTab == SpindLockerTab.Lobbymusik)
            {
                BuildCardsGrid(4, 188, 135, 2);
                for (int i = 0; i < 4; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool sel = selectedLobbyTrack == i;
                    bool own = ownedLobbyMusic[i];
                    using var bg = new SolidBrush(Color.FromArgb(24, 30, 48));
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(sel ? Pens.Cyan : Pens.SlateGray, r);
                    g.DrawString(GetLobbyTrackName(i), btnFont, Brushes.White, r.X + 12, r.Y + 14);
                    string st = sel ? T("WIRD ABGESPIELT", "PLAYING") : (own ? T("Klicken = Abspielen", "Click = Play") : T($"Kaufen: {lobbyTrackPrices[i]} C", $"Buy: {lobbyTrackPrices[i]} C"));
                    g.DrawString(st, btnFont, own ? Brushes.Cyan : Brushes.Gold, r.X + 12, r.Y + 76);
                }
            }
            else
            {
                BuildCardsGrid(3, 188, 145, 3);
                string[] exTitles =
                {
                    T("MAGAZIN +4", "MAGAZINE +4"),
                    T("RÜSTUNG +5%", "ARMOR +5%"),
                    T("COIN-PAKET +200", "COIN PACK +200")
                };
                string[] exSub =
                {
                    $"{T("Aktive Waffe", "Active Weapon")}: +4 {T("Schuss", "Ammo")}",
                    $"{T("Stufe", "Lv.")} {bonusArmorLevel + 1}",
                    T("Sofortige Münzen", "Instant Coins")
                };
                int[] exCosts = { GetWeaponMagUpgradeCost(equippedWeapon), 150, 0 };

                for (int i = 0; i < 3; i++)
                {
                    Rectangle r = currentCardRects[i];
                    using var bg = new SolidBrush(Color.FromArgb(24, 30, 48));
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(Pens.Gold, r);
                    g.DrawString(exTitles[i], btnFont, Brushes.White, r.X + 12, r.Y + 14);
                    g.DrawString(exSub[i], labelFont, Brushes.Silver, r.X + 12, r.Y + 42);
                    string act = i == 2 ? T("KOSTENLOS ABHOLEN", "CLAIM FREE") : T($"KAUFEN: {exCosts[i]} C", $"BUY: {exCosts[i]} C");
                    g.DrawString(act, btnFont, Brushes.LimeGreen, r.X + 12, r.Y + 96);
                }
            }
        }

        private void DrawSpinTab(Graphics g, Font secFont, Font btnFont, Font labelFont, Font bigBtnFont)
        {
            g.DrawString(T("SPIN-RAD • GEWINNE WAFFEN, SKINS & LOBBYMUSIK", "SPIN WHEEL • WIN WEAPONS, SKINS & LOBBY MUSIC"),
                secFont, Brushes.Violet, 30, 132);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sliceSweep = 360f / spinPrizes.Count;

            for (int i = 0; i < spinPrizes.Count; i++)
            {
                float startDeg = spinAngle + i * sliceSweep;
                using var sliceBrush = new SolidBrush(spinPrizes[i].Color);
                g.FillPie(sliceBrush, spinWheelRect, startDeg, sliceSweep);
                g.DrawPie(Pens.White, spinWheelRect, startDeg, sliceSweep);
            }

            using (var centerHoleBrush = new SolidBrush(Color.FromArgb(20, 24, 38)))
            {
                g.FillEllipse(centerHoleBrush,
                    spinWheelRect.X + spinWheelRect.Width * 0.35f,
                    spinWheelRect.Y + spinWheelRect.Height * 0.35f,
                    spinWheelRect.Width * 0.30f,
                    spinWheelRect.Height * 0.30f);
            }

            PointF topTip = new PointF(spinWheelRect.X + spinWheelRect.Width / 2f, spinWheelRect.Y + 8f);
            PointF leftPt = new PointF(topTip.X - 10f, spinWheelRect.Y - 10f);
            PointF rightPt = new PointF(topTip.X + 10f, spinWheelRect.Y - 10f);
            g.FillPolygon(Brushes.Gold, new[] { leftPt, rightPt, topTip });
            g.SmoothingMode = SmoothingMode.None;

            bool canSpin = !isSpinning && coins >= SpinCost;
            using (var sBg = new SolidBrush(canSpin ? Color.FromArgb(155, 65, 210) : Color.FromArgb(45, 45, 60)))
            {
                g.FillRectangle(sBg, spinBtnRect);
                g.DrawRectangle(Pens.White, spinBtnRect);
                string sTxt = isSpinning
                    ? T("DREHT...", "SPINNING...")
                    : T($"DREHEN ({SpinCost} COINS)", $"SPIN ({SpinCost} COINS)");
                SizeF sz = g.MeasureString(sTxt, bigBtnFont);
                g.DrawString(sTxt, bigBtnFont, Brushes.White,
                    spinBtnRect.X + (spinBtnRect.Width - sz.Width) / 2f,
                    spinBtnRect.Y + (spinBtnRect.Height - sz.Height) / 2f - 1);
            }

            if (!string.IsNullOrEmpty(lastSpinResultText))
                g.DrawString(lastSpinResultText, btnFont, Brushes.LimeGreen, spinBtnRect.X, spinBtnRect.Bottom + 14);

            g.DrawString(T("Mögliche Gewinne:", "Possible Prizes:"), btnFont, Brushes.Gold, spinBtnRect.X, spinBtnRect.Bottom + 44);
            for (int i = 0; i < spinPrizes.Count; i++)
            {
                int col = i / 4;
                int row = i % 4;
                string pLabel = IsEnglish ? spinPrizes[i].LabelEng : spinPrizes[i].LabelDe;
                g.DrawString($"• {pLabel}", labelFont, Brushes.White,
                    spinBtnRect.X + col * 190, spinBtnRect.Bottom + 70 + row * 22);
            }
        }

        private void DrawBattlePassTab(Graphics g, Font secFont, Font btnFont, Font labelFont)
        {
            int bpLv = GetBattlePassLevel();
            int curTierXp = battlePassXp % XpPerTier;
            float ratio = Math.Clamp(curTierXp / (float)XpPerTier, 0f, 1f);

            g.DrawString($"{T("BATTLE PASS", "BATTLE PASS")}  •  {T("Stufe", "Tier")} {bpLv}  |  XP: {battlePassXp}",
                secFont, Brushes.Gold, 30, 132);

            Rectangle xpBar = new Rectangle(30, 162, Math.Min(420, this.ClientSize.Width - 60), 16);
            using (var xpBgBrush = new SolidBrush(Color.FromArgb(30, 36, 54)))
            {
                g.FillRectangle(xpBgBrush, xpBar);
            }
            g.FillRectangle(Brushes.Gold, xpBar.X, xpBar.Y, (int)(xpBar.Width * ratio), xpBar.Height);
            g.DrawRectangle(Pens.White, xpBar);

            BuildCardsGrid(battlePassRewards.Count, 190, 118, 4);
            for (int i = 0; i < battlePassRewards.Count && i < currentCardRects.Count; i++)
            {
                BattlePassReward rew = battlePassRewards[i];
                Rectangle r = currentCardRects[i];
                bool unlocked = bpLv >= rew.Tier;
                bool claimed = claimedPassTiers[i];

                Color borderCol = claimed ? Color.SlateGray : (unlocked ? Color.LimeGreen : Color.DimGray);
                using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                using var pen = new Pen(borderCol, 2f);
                g.FillRectangle(bg, r);
                g.DrawRectangle(pen, r);

                g.DrawString($"{T("Stufe", "Tier")} {rew.Tier}", btnFont, Brushes.Cyan, r.X + 10, r.Y + 10);
                g.DrawString(IsEnglish ? rew.TitleEng : rew.TitleDe, labelFont, Brushes.White, r.X + 10, r.Y + 38);

                string status = claimed
                    ? T("ABGEHOLT", "CLAIMED")
                    : (unlocked ? T("KLICK = ABHOLEN", "CLICK = CLAIM") : T("GESPERRT", "LOCKED"));
                Brush stBrush = claimed ? Brushes.Silver : (unlocked ? Brushes.LimeGreen : Brushes.IndianRed);
                g.DrawString(status, btnFont, stBrush, r.X + 10, r.Y + 84);
            }
        }

        private void DrawEinstellungTab(Graphics g, Font secFont, Font btnFont, Font labelFont)
        {
            SettingsCategory[] cats = { SettingsCategory.Grafik, SettingsCategory.Audio, SettingsCategory.Gameplay };
            string[] catNames = { T("GRAFIK", "GRAPHICS"), "AUDIO", "GAMEPLAY" };

            for (int i = 0; i < 3; i++)
            {
                bool sel = activeSettingsCat == cats[i];
                using var cBg = new SolidBrush(sel ? Color.FromArgb(0, 150, 136) : Color.FromArgb(26, 30, 46));
                g.FillRectangle(cBg, settingsCatRects[i]);
                g.DrawRectangle(sel ? Pens.White : Pens.SlateGray, settingsCatRects[i]);
                SizeF sz = g.MeasureString(catNames[i], btnFont);
                g.DrawString(catNames[i], btnFont, Brushes.White,
                    settingsCatRects[i].X + (settingsCatRects[i].Width - sz.Width) / 2f,
                    settingsCatRects[i].Y + (settingsCatRects[i].Height - sz.Height) / 2f);
            }

            if (activeSettingsCat == SettingsCategory.Grafik)
            {
                string styleLabel = IsEnglish ? crosshairStyleNamesEng[crosshairStyleIndex] : crosshairStyleNamesDe[crosshairStyleIndex];
                string colorLabel = IsEnglish ? crosshairColorNamesEng[crosshairColorIndex] : crosshairColorNamesDe[crosshairColorIndex];
                string sizeLabel = crosshairSizeLevel switch { 1 => T("Klein", "Small"), 3 => T("Groß", "Large"), _ => T("Mittel", "Medium") };

                g.DrawString(T("GRAFIK & CUSTOM FADENKREUZ", "GRAPHICS & CUSTOM CROSSHAIR"), secFont, Brushes.Cyan, 30, 184);
                DrawSettingButton(g, setBtnA, btnFont, fullscreenF11 ? "F11 VOLLBILD: AN" : "F11 VOLLBILD: AUS", fullscreenF11);
                DrawSettingButton(g, setBtnB, btnFont, vsyncEnabled ? "V-SYNC: AN" : "V-SYNC: AUS", vsyncEnabled);
                DrawSettingButton(g, setBtnC, btnFont, $"{T("STIL", "STYLE")}: {styleLabel}", true);
                DrawSettingButton(g, setBtnD, btnFont, $"{T("FARBE", "COLOR")}: {colorLabel}", true);
                DrawSettingButton(g, setBtnE, btnFont, $"{T("GRÖSSE", "SIZE")}: {sizeLabel}", true);
                DrawSettingButton(g, setBtnF, btnFont, bloodEffects ? T("BLUT: AN", "BLOOD: ON") : T("BLUT: AUS", "BLOOD: OFF"), bloodEffects);

                int prevBoxX = Math.Max(570, setBtnB.Right + 28);
                int prevBoxW = Math.Max(160, this.ClientSize.Width - prevBoxX - 30);
                Rectangle chPreviewBox = new Rectangle(prevBoxX, 212, prevBoxW, 210);
                using var chBg = new SolidBrush(Color.FromArgb(20, 26, 40));
                g.FillRectangle(chBg, chPreviewBox);
                g.DrawRectangle(Pens.SlateGray, chPreviewBox);
                g.DrawString(T("FADENKREUZ-VORSCHAU:", "CROSSHAIR PREVIEW:"), labelFont, Brushes.Silver, chPreviewBox.X + 12, chPreviewBox.Y + 10);
                DrawCustomCrosshairAt(g, chPreviewBox.X + chPreviewBox.Width / 2f, chPreviewBox.Y + chPreviewBox.Height / 2f + 10f);
            }
            else if (activeSettingsCat == SettingsCategory.Audio)
            {
                int volPct = (int)MathF.Round(masterVolume * 100f);
                g.DrawString($"{T("AUDIO & LOBBYMUSIK", "AUDIO & LOBBY MUSIC")} ({volPct}%)", secFont, Brushes.Cyan, 30, 188);
                DrawSettingButton(g, setBtnA, btnFont, T("LAUTSTÄRKE -10%", "VOLUME -10%"), true);
                DrawSettingButton(g, setBtnB, btnFont, T("LAUTSTÄRKE +10%", "VOLUME +10%"), true);
                DrawSettingButton(g, setBtnC, btnFont, lobbyMusicEnabled ? T("LOBBYMUSIK: AN", "LOBBY MUSIC: ON") : T("LOBBYMUSIK: AUS", "LOBBY MUSIC: OFF"), lobbyMusicEnabled);
                DrawSettingButton(g, setBtnD, btnFont, $"{T("TRACK", "TRACK")}: {GetLobbyTrackName(selectedLobbyTrack)}", true);
            }
            else
            {
                g.DrawString(T("GAMEPLAY & SPRACHE", "GAMEPLAY & LANGUAGE"), secFont, Brushes.Cyan, 30, 188);
                DrawSettingButton(g, setBtnA, btnFont, !IsEnglish ? T("SPRACHE: DEUTSCH", "LANG: GERMAN") : "DEUTSCH", !IsEnglish);
                DrawSettingButton(g, setBtnB, btnFont, IsEnglish ? "ENGLISH (ACTIVE)" : "ENGLISH", IsEnglish);
                DrawSettingButton(g, setBtnC, btnFont, autoReload ? T("AUTO-NACHLADEN: AN", "AUTO-RELOAD: ON") : T("AUTO-NACHLADEN: AUS", "AUTO-RELOAD: OFF"), autoReload);
                DrawSettingButton(g, setBtnD, btnFont, T("TUTORIAL RESET", "RESET TUTORIAL"), false);
            }

            g.DrawString(T("F11 = Vollbild | WASD = Gehen | SHIFT = Rennen | LEERTASTE = Dash | R = Nachladen | ESC = Pause",
                "F11 = Fullscreen | WASD = Move | SHIFT = Sprint | SPACE = Dash | R = Reload | ESC = Pause"),
                labelFont, Brushes.LightGray, 30, setBtnF.Bottom + 10);
        }

        private void DrawCustomCrosshairAt(Graphics g, float x, float y)
        {
            if (!showCrosshair) return;

            Color col = reloadTimer > 0 ? Color.Orange : crosshairPalette[Math.Clamp(crosshairColorIndex, 0, crosshairPalette.Length - 1)];
            float scale = crosshairSizeLevel switch { 1 => 0.72f, 3 => 1.38f, _ => 1.0f };
            float gap = 4f * scale;
            float len = 10f * scale;
            float thickness = Math.Max(1.6f, 2.0f * scale);

            using var pen = new Pen(col, thickness);
            using var dotBrush = new SolidBrush(col);
            CrosshairStyle style = (CrosshairStyle)Math.Clamp(crosshairStyleIndex, 0, 3);

            switch (style)
            {
                case CrosshairStyle.Dot:
                    float dotR = 3.2f * scale;
                    g.FillEllipse(dotBrush, x - dotR, y - dotR, dotR * 2f, dotR * 2f);
                    break;

                case CrosshairStyle.CircleDot:
                    float rad = 11f * scale;
                    g.DrawEllipse(pen, x - rad, y - rad, rad * 2f, rad * 2f);
                    g.FillEllipse(dotBrush, x - 2.2f, y - 2.2f, 4.4f, 4.4f);
                    break;

                case CrosshairStyle.XScope:
                    float d0 = gap * 0.85f;
                    float d1 = d0 + len * 0.85f;
                    g.DrawLine(pen, x - d1, y - d1, x - d0, y - d0);
                    g.DrawLine(pen, x + d0, y - d0, x + d1, y - d1);
                    g.DrawLine(pen, x - d1, y + d1, x - d0, y + d0);
                    g.DrawLine(pen, x + d0, y + d0, x + d1, y + d1);
                    g.FillEllipse(dotBrush, x - 1.8f, y - 1.8f, 3.6f, 3.6f);
                    break;

                default:
                    g.DrawLine(pen, x - gap - len, y, x - gap, y);
                    g.DrawLine(pen, x + gap, y, x + gap + len, y);
                    g.DrawLine(pen, x, y - gap - len, x, y - gap);
                    g.DrawLine(pen, x, y + gap, x, y + gap + len);
                    break;
            }

            if (reloadTimer > 0 && reloadDuration > 0)
            {
                float prog = 1f - (reloadTimer / (float)reloadDuration);
                g.FillRectangle(Brushes.Black, x - 14f, y + 15f, 28f, 5f);
                g.FillRectangle(Brushes.Gold, x - 14f, y + 15f, 28f * prog, 5f);
            }
        }

        private static void DrawSettingButton(Graphics g, Rectangle rect, Font font, string text, bool active)
        {
            using var bg = new SolidBrush(active ? Color.FromArgb(38, 125, 185) : Color.FromArgb(32, 36, 52));
            g.FillRectangle(bg, rect);
            g.DrawRectangle(Pens.White, rect);
            SizeF sz = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.White,
                rect.X + (rect.Width - sz.Width) / 2f,
                rect.Y + (rect.Height - sz.Height) / 2f);
        }

        private void DrawHauptTab(Graphics g, Font secFont, Font labelFont, Font inputFont, Font btnFont)
        {
            g.DrawString(T("ACCOUNT / GAMERTAG:", "ACCOUNT / GAMERTAG:"), secFont, Brushes.Cyan, tagBoxRect.X, tagBoxRect.Y - 28);

            using (var boxBg = new SolidBrush(Color.FromArgb(26, 30, 46)))
            using (var boxBorder = new Pen(Color.SlateGray, 2.5f))
            {
                g.FillRectangle(boxBg, tagBoxRect);
                g.DrawRectangle(boxBorder, tagBoxRect);
            }

            string tagText = string.IsNullOrWhiteSpace(gamerTag) ? T("Spieler", "Player") : gamerTag;
            g.DrawString(tagText, inputFont, Brushes.White, tagBoxRect.X + 10, tagBoxRect.Y + 9);

            if (!string.IsNullOrEmpty(tagWarning))
                g.DrawString(tagWarning, labelFont, Brushes.OrangeRed, tagBoxRect.X, tagBoxRect.Bottom + 4);

            using (var startBg = new SolidBrush(Color.FromArgb(38, 165, 85)))
            using (var startBorder = new Pen(Color.White, 3f))
            {
                g.FillRectangle(startBg, startBtnRect);
                g.DrawRectangle(startBorder, startBtnRect);
            }

            string startLabel = T("SPIEL STARTEN", "START GAME");
            SizeF sSize = g.MeasureString(startLabel, btnFont);
            g.DrawString(startLabel, btnFont, Brushes.White,
                startBtnRect.X + (startBtnRect.Width - sSize.Width) / 2f,
                startBtnRect.Y + (startBtnRect.Height - sSize.Height) / 2f - 1);

            using (var mpBg = new SolidBrush(Color.FromArgb(45, 130, 230)))
            using (var mpBorder = new Pen(Color.Cyan, 2.5f))
            {
                g.FillRectangle(mpBg, multiplayerBtnRect);
                g.DrawRectangle(mpBorder, multiplayerBtnRect);
            }

            string mpLabel = "MULTIPLAYER";
            SizeF mpSize = g.MeasureString(mpLabel, btnFont);
            g.DrawString(mpLabel, btnFont, Brushes.White,
                multiplayerBtnRect.X + (multiplayerBtnRect.Width - mpSize.Width) / 2f,
                multiplayerBtnRect.Y + (multiplayerBtnRect.Height - mpSize.Height) / 2f - 1);

            int px = 365;
            int py = 175;
            int pw = Math.Max(300, this.ClientSize.Width - px - 30);
            using var panelBg = new SolidBrush(Color.FromArgb(24, 28, 42));
            using var panelPen = new Pen(Color.FromArgb(65, 80, 115), 2f);
            Rectangle previewPanel = new Rectangle(px, py, pw, 210);
            g.FillRectangle(panelBg, previewPanel);
            g.DrawRectangle(panelPen, previewPanel);

            if (humanRigs[equippedSkin]?.FullPreview != null)
                g.DrawImage(humanRigs[equippedSkin]!.FullPreview!, px + 20, py + 24, 96, 96);
            if (zombieRig?.FullPreview != null)
                g.DrawImage(zombieRig.FullPreview, px + 132, py + 24, 96, 96);

            g.DrawString($"Skin: {GetSkinName(equippedSkin)}", secFont, Brushes.White, px + 242, py + 24);
            g.DrawString($"{T("Waffe", "Weapon")}: {GetWeaponName(equippedWeapon)} (Lv.{weaponLevels[equippedWeapon]})",
                labelFont, Brushes.Gold, px + 242, py + 58);
            g.DrawString($"Pass Lv.{GetBattlePassLevel()} ({battlePassXp} XP)  |  Max-HP: {GetMaxHpValue()}",
                labelFont, Brushes.Cyan, px + 242, py + 84);
            g.DrawString($"{T("Magazin", "Mag")}: {GetWeaponMagCapacity(equippedWeapon)}  |  {T("Musik", "Music")}: {GetLobbyTrackName(selectedLobbyTrack)}",
                labelFont, Brushes.Silver, px + 242, py + 110);
            g.DrawString("F11 = Fullscreen  •  SPACE = Dash  •  R = Reload  •  ESC = Pause",
                labelFont, Brushes.LightGray, px + 20, py + 168);
        }

        private void DrawSkinTab(Graphics g, Font secFont, Font titleFont, Font labelFont)
        {
            g.DrawString(T("PIXEL-SKINS AUSWÄHLEN", "SELECT PIXEL SKINS"), secFont, Brushes.Cyan, 30, 138);
            BuildCardsGrid(skinNamesDe.Length, 176, 155, 3);

            for (int i = 0; i < currentCardRects.Count; i++)
            {
                Rectangle r = currentCardRects[i];
                bool isEquipped = equippedSkin == i;
                bool isOwned = ownedSkins[i];

                using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                using var border = new Pen(isEquipped ? Color.LimeGreen : (isOwned ? Color.DodgerBlue : Color.DimGray), isEquipped ? 3f : 2f);
                g.FillRectangle(bg, r);
                g.DrawRectangle(border, r);

                if (humanRigs[i]?.FullPreview != null)
                    g.DrawImage(humanRigs[i]!.FullPreview!, r.X + 14, r.Y + 22, 60, 60);

                g.DrawString(GetSkinName(i), titleFont, Brushes.White, r.X + 86, r.Y + 26);
                string status = isEquipped
                    ? T("AUSGERÜSTET", "EQUIPPED")
                    : (isOwned ? T("Klicken zum Ausrüsten", "Click to Equip") : $"Shop / Spin: {skinPrices[i]} C");
                Brush stBrush = isEquipped ? Brushes.LimeGreen : (isOwned ? Brushes.Cyan : Brushes.Gold);
                g.DrawString(status, labelFont, stBrush, r.X + 86, r.Y + 58);
            }
        }

        private void DrawWaffenkammerTab(Graphics g, Font secFont, Font titleFont, Font labelFont)
        {
            g.DrawString(T("WAFFENKAMMER (WAFFENSTUFEN & AUSRÜSTUNG)", "ARMORY (WEAPON LEVELS & GEAR)"), secFont, Brushes.Cyan, 30, 138);
            BuildCardsGrid(weaponNamesDe.Length, 176, 155, 2);

            for (int i = 0; i < currentCardRects.Count; i++)
            {
                Rectangle r = currentCardRects[i];
                bool isEquipped = equippedWeapon == i;
                bool isOwned = ownedWeapons[i];

                using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                using var border = new Pen(isEquipped ? Color.LimeGreen : Color.SlateGray, isEquipped ? 3f : 2f);
                g.FillRectangle(bg, r);
                g.DrawRectangle(border, r);

                g.DrawString($"{GetWeaponName(i)}  [{T("Stufe", "Lv.")} {weaponLevels[i]}]", titleFont, Brushes.White, r.X + 16, r.Y + 18);
                g.DrawString(T($"Schaden: {GetWeaponDamage(i)}  |  Magazin: {GetWeaponMagCapacity(i)}",
                    $"Damage: {GetWeaponDamage(i)}  |  Mag: {GetWeaponMagCapacity(i)}"),
                    labelFont, Brushes.Gold, r.X + 16, r.Y + 48);

                string wStatus = isEquipped
                    ? T("WAFFE AKTIV", "WEAPON ACTIVE")
                    : (isOwned ? T("Klicken zum Wählen", "Click to Select") : $"Shop / Spin: {weaponPrices[i]} C");
                g.DrawString(wStatus, labelFont, isEquipped ? Brushes.LimeGreen : Brushes.Cyan, r.X + 16, r.Y + 76);

                if (i < gearNamesDe.Length)
                {
                    bool gEq = equippedGear == i;
                    string gTxt = $"{T("Ausrüstung", "Gear")}: {GetGearName(i)}" +
                        (gEq ? T(" [AKTIV]", " [ACTIVE]") : (ownedGear[i] ? T(" [Klicken]", " [Click]") : $" ({gearPrices[i]} C)"));
                    g.DrawString(gTxt, labelFont, gEq ? Brushes.LimeGreen : Brushes.Silver, r.X + 16, r.Y + 110);
                }
            }
        }

        private void DrawKarriereTab(Graphics g, Font secFont, Font statFont, Font labelFont)
        {
            string activeTag = string.IsNullOrWhiteSpace(gamerTag) ? T("Spieler", "Player") : gamerTag;
            g.DrawString($"{T("KARRIERE", "CAREER")}  •  {activeTag}", secFont, Brushes.Cyan, 30, 138);

            using var panelBg = new SolidBrush(Color.FromArgb(26, 30, 46));
            using var panelPen = new Pen(Color.SlateGray, 2f);
            Rectangle box = new Rectangle(30, 178, Math.Min(580, this.ClientSize.Width - 60), 255);
            g.FillRectangle(panelBg, box);
            g.DrawRectangle(panelPen, box);

            g.DrawString($"{T("Gespielte Runden", "Games Played")}:   {gamesPlayed}", statFont, Brushes.White, 52, 200);
            g.DrawString($"{T("Zombie-Kills gesamt", "Total Zombie Kills")}: {totalKills}", statFont, Brushes.LimeGreen, 52, 238);
            g.DrawString($"{T("Beste Welle", "Best Wave")}:         {bestWave}", statFont, Brushes.OrangeRed, 52, 276);
            g.DrawString($"{T("Punkte gesamt", "Total Score")}:       {totalScore}", statFont, Brushes.Gold, 52, 314);
            g.DrawString($"Coins: {coins}  |  BattlePass Lv.{GetBattlePassLevel()} ({battlePassXp} XP)",
                labelFont, Brushes.LightGray, 52, 368);
        }

        private void DrawShopTab(Graphics g, Font secFont, Font subTabFont, Font labelFont)
        {
            g.DrawString("SHOP", secFont, Brushes.OrangeRed, 30, 114);

            string[] subLabels = { "UPGRADES", "SKINS", T("WAFFEN", "WEAPONS"), T("AUSRÜSTUNG", "GEAR") };
            ShopSubTab[] subEnums = { ShopSubTab.Upgrades, ShopSubTab.Skins, ShopSubTab.Waffen, ShopSubTab.Ausruestung };

            for (int i = 0; i < 4; i++)
            {
                bool sel = activeShopTab == subEnums[i];
                using var bg = new SolidBrush(sel ? Color.FromArgb(215, 95, 30) : Color.FromArgb(26, 30, 46));
                using var border = new Pen(sel ? Color.White : Color.SlateGray, sel ? 2.5f : 1.5f);
                g.FillRectangle(bg, shopSubTabRects[i]);
                g.DrawRectangle(border, shopSubTabRects[i]);

                SizeF sz = g.MeasureString(subLabels[i], subTabFont);
                g.DrawString(subLabels[i], subTabFont, Brushes.White,
                    shopSubTabRects[i].X + (shopSubTabRects[i].Width - sz.Width) / 2f,
                    shopSubTabRects[i].Y + (shopSubTabRects[i].Height - sz.Height) / 2f);
            }

            if (activeShopTab == ShopSubTab.Upgrades)
            {
                BuildCardsGrid(5, 186, 130, 3);
                string[] uTitles =
                {
                    T($"Feuerrate Lv.{fireRateLevel}", $"Fire Rate Lv.{fireRateLevel}"),
                    T($"Tempo Lv.{speedLevel}", $"Speed Lv.{speedLevel}"),
                    $"Max-HP Lv.{maxHpLevel}",
                    T($"Ausdauer Lv.{staminaRegenLevel}", $"Stamina Lv.{staminaRegenLevel}"),
                    T($"Coin-Boost Lv.{coinBoostLevel}", $"Coin Boost Lv.{coinBoostLevel}")
                };
                string[] uDesc =
                {
                    T("Schneller schießen & laden", "Faster fire & reload"),
                    T("Schneller laufen", "Faster walk speed"),
                    T("+25 Max-HP", "+25 Max HP"),
                    T("+25% Ausdauer-Regen", "+25% Stamina regen"),
                    T("+25% Münzen pro Kill/Welle", "+25% Coins per kill/wave")
                };
                int[] uCosts =
                {
                    GetUpgradeCost(fireRateLevel),
                    GetUpgradeCost(speedLevel),
                    GetUpgradeCost(maxHpLevel),
                    GetUpgradeCost(staminaRegenLevel),
                    GetUpgradeCost(coinBoostLevel)
                };

                for (int i = 0; i < 5 && i < currentCardRects.Count; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool canBuy = coins >= uCosts[i];
                    using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                    using var border = new Pen(canBuy ? Color.Gold : Color.DimGray, 2f);
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(border, r);

                    g.DrawString(uTitles[i], subTabFont, Brushes.White, r.X + 12, r.Y + 12);
                    g.DrawString(uDesc[i], labelFont, Brushes.Silver, r.X + 12, r.Y + 40);
                    g.DrawString(T($"KAUFEN: {uCosts[i]} C", $"BUY: {uCosts[i]} C"),
                        labelFont, canBuy ? Brushes.Gold : Brushes.IndianRed, r.X + 12, r.Y + 92);
                }
            }
            else if (activeShopTab == ShopSubTab.Skins)
            {
                BuildCardsGrid(skinNamesDe.Length, 186, 148, 3);
                for (int i = 0; i < skinNamesDe.Length; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool owned = ownedSkins[i];
                    bool canBuy = coins >= skinPrices[i];

                    using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                    using var border = new Pen(owned ? Color.LimeGreen : (canBuy ? Color.Gold : Color.DimGray), 2f);
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(border, r);

                    if (humanRigs[i]?.FullPreview != null)
                        g.DrawImage(humanRigs[i]!.FullPreview!, r.X + 12, r.Y + 18, 52, 52);

                    g.DrawString(GetSkinName(i), subTabFont, Brushes.White, r.X + 72, r.Y + 20);
                    string action = equippedSkin == i
                        ? T("AUSGERÜSTET", "EQUIPPED")
                        : (owned ? T("AUSRÜSTEN", "EQUIP") : T($"KAUFEN: {skinPrices[i]} C", $"BUY: {skinPrices[i]} C"));
                    g.DrawString(action, labelFont, owned || canBuy ? Brushes.Gold : Brushes.IndianRed, r.X + 72, r.Y + 52);
                }
            }
            else if (activeShopTab == ShopSubTab.Waffen)
            {
                BuildCardsGrid(weaponNamesDe.Length, 186, 162, 2);
                for (int i = 0; i < weaponNamesDe.Length; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool owned = ownedWeapons[i];
                    int lvCost = GetWeaponUpgradeCost(i);
                    int magCost = GetWeaponMagUpgradeCost(i);

                    using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                    using var border = new Pen(equippedWeapon == i ? Color.LimeGreen : Color.Gold, 2f);
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(border, r);

                    g.DrawString($"{GetWeaponName(i)}  [Lv.{weaponLevels[i]} | Mag+{weaponMagBonus[i] * 4}]", subTabFont, Brushes.White, r.X + 12, r.Y + 14);
                    g.DrawString($"{T("Schaden", "Dmg")}: {GetWeaponDamage(i)}  |  {T("Mag", "Mag")}: {GetWeaponMagCapacity(i)}",
                        labelFont, Brushes.Silver, r.X + 12, r.Y + 42);

                    if (!owned)
                    {
                        bool canBuy = coins >= weaponPrices[i];
                        g.DrawString(T($"KAUFEN: {weaponPrices[i]} Coins", $"BUY: {weaponPrices[i]} Coins"), labelFont,
                            canBuy ? Brushes.Gold : Brushes.IndianRed, r.X + 12, r.Y + 86);
                    }
                    else
                    {
                        g.DrawString(equippedWeapon == i ? T("AUSGERÜSTET", "EQUIPPED") : T("Klicken = Ausrüsten", "Click = Equip"),
                            labelFont, Brushes.LimeGreen, r.X + 12, r.Y + 72);
                        g.DrawString(T($"Stufe +1: {lvCost} C  |  Mag+4: {magCost} C", $"Lv+1: {lvCost} C  |  Mag+4: {magCost} C"),
                            labelFont, Brushes.Cyan, r.X + 12, r.Y + 104);
                    }
                }
            }
            else if (activeShopTab == ShopSubTab.Ausruestung)
            {
                BuildCardsGrid(gearNamesDe.Length, 186, 155, 2);
                string[] gDesc =
                {
                    T("Basis-Schutz (100% Schaden)", "Base protection (100% damage)"),
                    T("-22% Schaden & +30 Max-HP", "-22% damage & +30 Max HP"),
                    T("-30% Schaden, +50 Max-HP & +40 Heal", "-30% damage, +50 Max HP & +40 Heal"),
                    T("-45% Schaden & +85 Max-HP", "-45% damage & +85 Max HP")
                };

                for (int i = 0; i < gearNamesDe.Length; i++)
                {
                    Rectangle r = currentCardRects[i];
                    bool owned = ownedGear[i];
                    bool canBuy = coins >= gearPrices[i];

                    using var bg = new SolidBrush(Color.FromArgb(26, 30, 46));
                    using var border = new Pen(equippedGear == i ? Color.LimeGreen : (owned || canBuy ? Color.Gold : Color.DimGray), 2f);
                    g.FillRectangle(bg, r);
                    g.DrawRectangle(border, r);

                    g.DrawString($"{GetGearName(i)} (Extra-Schutz Lv.{bonusArmorLevel})", subTabFont, Brushes.White, r.X + 12, r.Y + 14);
                    g.DrawString(gDesc[i], labelFont, Brushes.Silver, r.X + 12, r.Y + 42);

                    string action = equippedGear == i
                        ? T("AUSGERÜSTET", "EQUIPPED")
                        : (owned ? T("AUSRÜSTEN", "EQUIP") : T($"KAUFEN: {gearPrices[i]} C", $"BUY: {gearPrices[i]} C"));
                    g.DrawString(action, labelFont, owned || canBuy ? Brushes.Gold : Brushes.IndianRed, r.X + 12, r.Y + 76);
                    g.DrawString(T("Unten klicken: +5% Extra-Schutz (150 C)", "Click lower: +5% Extra Armor (150 C)"),
                        labelFont, Brushes.Cyan, r.X + 12, r.Y + 106);
                }
            }
        }

        private void DrawGameplay(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            float shakeX = 0f, shakeY = 0f;
            if (screenShakeEnabled && screenShakeMag > 0.1f)
            {
                shakeX = ((float)rng.NextDouble() * 2f - 1f) * screenShakeMag;
                shakeY = ((float)rng.NextDouble() * 2f - 1f) * screenShakeMag;
            }

            GraphicsState worldSaved = g.Save();
            g.TranslateTransform(shakeX, shakeY);

            Color baseGrass = currentMap switch
            {
                MapType.RasenFluss => Color.FromArgb(52, 126, 48),
                MapType.WaldBach => Color.FromArgb(26, 74, 32),
                MapType.SumpfKanal => Color.FromArgb(72, 88, 40),
                _ => Color.FromArgb(124, 130, 58)
            };

            Color bankColor = currentMap == MapType.SumpfKanal
                ? Color.FromArgb(50, 42, 28)
                : Color.FromArgb(106, 82, 50);

            int wR = currentMap == MapType.WaldBach ? 36 : 42;
            int wG = currentMap == MapType.SumpfKanal ? 92 : (currentMap == MapType.WaldBach ? 112 : 134);
            int wB = currentMap == MapType.SumpfKanal ? 64 : (currentMap == MapType.WaldBach ? 174 : 204);
            Color waterColor = Color.FromArgb(wR, wG, wB);

            int dR = currentMap == MapType.WaldBach ? 24 : 28;
            int dG = currentMap == MapType.SumpfKanal ? 68 : (currentMap == MapType.WaldBach ? 84 : 102);
            int dB = currentMap == MapType.SumpfKanal ? 48 : (currentMap == MapType.WaldBach ? 138 : 168);
            Color deepWaterColor = Color.FromArgb(dR, dG, dB);

            g.Clear(baseGrass);

            using (var patchBrush = new SolidBrush(Color.FromArgb(26, 18, 28, 16)))
            using (var sunPatchBrush = new SolidBrush(Color.FromArgb(20, 225, 240, 145)))
            {
                for (int i = 0; i < 28; i++)
                {
                    int px = ((i * 197 + (int)riverCurveSeed * 40) % Math.Max(80, w - 60)) + 20;
                    int py = ((i * 131 + i * i * 7) % Math.Max(80, h - 60)) + 20;
                    g.FillEllipse(i % 2 == 0 ? patchBrush : sunPatchBrush, px, py, 48 + (i % 4) * 14, 30 + (i % 3) * 10);
                }
            }

            foreach (GrassTuft gt in grassTufts)
            {
                float sway = MathF.Sin(gt.SwayPhase) * 1.8f;
                using var gb = new SolidBrush(gt.Color);
                g.FillRectangle(gb, gt.X + sway, gt.Y, gt.W, gt.H);
            }

            using var bankBrush = new SolidBrush(bankColor);
            using var waterBrush = new SolidBrush(waterColor);
            using var deepWaterBrush = new SolidBrush(deepWaterColor);
            using var wavePen = new Pen(Color.FromArgb(165, 215, 245, 255), 2.2f);
            using var glintPen = new Pen(Color.FromArgb(110, 255, 255, 255), 1.4f);

            if (horizontalRiver)
            {
                for (int x = 0; x < w; x += 12)
                {
                    float cy = GetRiverCenterAt(x + 6);
                    float topY = cy - riverWidth * 0.5f;
                    g.FillRectangle(bankBrush, x, topY - 9f, 13f, riverWidth + 18f);
                    g.FillRectangle(waterBrush, x, topY, 13f, riverWidth);
                    g.FillRectangle(deepWaterBrush, x, cy - riverWidth * 0.22f, 13f, riverWidth * 0.44f);
                }

                for (int x = 20; x < w - 30; x += 54)
                {
                    float wy = GetRiverCenterAt(x) + MathF.Sin(x * 0.04f + waterWavePhase) * (riverWidth * 0.24f);
                    g.DrawLine(wavePen, x, wy, x + 28, wy);
                    g.DrawLine(glintPen, x + 8, wy - 5f, x + 20, wy - 5f);
                }
            }
            else
            {
                for (int y = 0; y < h; y += 12)
                {
                    float cx = GetRiverCenterAt(y + 6);
                    float leftX = cx - riverWidth * 0.5f;
                    g.FillRectangle(bankBrush, leftX - 9f, y, riverWidth + 18f, 13f);
                    g.FillRectangle(waterBrush, leftX, y, riverWidth, 13f);
                    g.FillRectangle(deepWaterBrush, cx - riverWidth * 0.22f, y, riverWidth * 0.44f, 13f);
                }

                for (int y = 20; y < h - 30; y += 54)
                {
                    float wx = GetRiverCenterAt(y) + MathF.Cos(y * 0.04f + waterWavePhase) * (riverWidth * 0.24f);
                    g.DrawLine(wavePen, wx, y, wx, y + 28);
                    g.DrawLine(glintPen, wx - 5f, y + 8, wx - 5f, y + 20);
                }
            }

            if (bloodEffects)
            {
                foreach (BloodSplatter sp in bloodSplatters)
                {
                    using var sb = new SolidBrush(sp.Color);
                    g.FillEllipse(sb, sp.X - sp.Size / 2f, sp.Y - sp.Size / 2f, sp.Size, sp.Size);
                }
            }

            foreach (PickupItem pk in pickups)
            {
                float bob = MathF.Sin(pk.BobPhase) * 3f;
                if (pk.Kind == PickupKind.Coin)
                {
                    g.FillEllipse(Brushes.Gold, pk.X - 8f, pk.Y - 8f + bob, 16f, 16f);
                    g.DrawEllipse(Pens.White, pk.X - 8f, pk.Y - 8f + bob, 16f, 16f);
                }
                else
                {
                    g.FillRectangle(Brushes.White, pk.X - 9f, pk.Y - 9f + bob, 18f, 18f);
                    g.FillRectangle(Brushes.LimeGreen, pk.X - 3f, pk.Y - 7f + bob, 6f, 14f);
                    g.FillRectangle(Brushes.LimeGreen, pk.X - 7f, pk.Y - 3f + bob, 14f, 6f);
                }
            }

            foreach (ShellCasing sc in shellCasings)
            {
                GraphicsState saved = g.Save();
                g.TranslateTransform(sc.X, sc.Y);
                g.RotateTransform(sc.Angle * (180f / MathF.PI));
                g.FillRectangle(Brushes.Gold, -3f, -1.5f, 6f, 3f);
                g.Restore(saved);
            }

            foreach (TerrainPixel tp in terrainPixels)
            {
                using var tpb = new SolidBrush(tp.Color);
                g.FillRectangle(tpb, tp.X - tp.Size / 2f, tp.Y - tp.Size / 2f, tp.Size, tp.Size);
            }

            foreach (SparkParticle sp in sparkParticles)
            {
                using var spb = new SolidBrush(sp.Color);
                g.FillRectangle(spb, sp.X - sp.Size / 2f, sp.Y - sp.Size / 2f, sp.Size, sp.Size);
            }

            if (bloodEffects)
            {
                foreach (BloodParticle p in bloodParticles)
                {
                    using var pb = new SolidBrush(p.Color);
                    g.FillRectangle(pb, p.X - p.Size / 2f, p.Y - p.Size / 2f, p.Size, p.Size);
                }
            }

            foreach (Bullet b in bullets)
            {
                using var bb = new SolidBrush(b.Color);
                g.FillRectangle(bb, b.X - 4, b.Y - 4, 8, 8);
            }

            foreach (MuzzleFlash mf in muzzleFlashes)
            {
                float alphaRatio = Math.Clamp(mf.Life / (float)Math.Max(1, mf.MaxLife), 0.2f, 1f);
                int outerA = (int)(195 * alphaRatio);
                using var outerBrush = new SolidBrush(Color.FromArgb(outerA, 255, 165, 30));
                using var innerBrush = new SolidBrush(Color.FromArgb(245, 255, 245, 160));
                float r = mf.Radius * (0.75f + 0.35f * alphaRatio);
                g.FillEllipse(outerBrush, mf.X - r, mf.Y - r, r * 2f, r * 2f);
                g.FillEllipse(innerBrush, mf.X - r * 0.45f, mf.Y - r * 0.45f, r * 0.9f, r * 0.9f);
            }

            foreach (Zombie z in zombies)
            {
                DrawCharacterAnimated(
                    g,
                    zombieRig,
                    z.X,
                    z.Y,
                    z.Size,
                    z.Angle,
                    z.WalkPhase,
                    moving: true,
                    sprinting: false,
                    zombieReachArms: true);

                if (z.HitFlashTicks > 0)
                {
                    using var flashBrush = new SolidBrush(Color.FromArgb(130, 255, 80, 80));
                    g.FillRectangle(flashBrush, z.X - z.Size / 2f, z.Y - z.Size / 2f, z.Size, z.Size);
                }

                if (z.Hp < z.MaxHp)
                {
                    float bw = z.Size;
                    float bx = z.X - bw / 2f;
                    float by = z.Y - z.Size / 2f - 9f;
                    float ratio = Math.Clamp(z.Hp / (float)Math.Max(1, z.MaxHp), 0f, 1f);
                    g.FillRectangle(Brushes.Maroon, bx, by, bw, 5);
                    g.FillRectangle(Brushes.Red, bx, by, bw * ratio, 5);
                }
            }

            if (isSwimmingNow)
            {
                using var ripplePen = new Pen(Color.FromArgb(170, 220, 245, 255), 2f);
                float rExpand = 19f + MathF.Sin(walkAnimPhase) * 4f;
                g.DrawEllipse(ripplePen, playerX - rExpand, playerY - rExpand * 0.7f + 6f, rExpand * 2f, rExpand * 1.4f);
            }

            if (dashTimer > 0)
            {
                using var dashAura = new SolidBrush(Color.FromArgb(95, 80, 235, 255));
                g.FillEllipse(dashAura, playerX - 24f, playerY - 24f, 48f, 48f);
            }

            using var tagFont = new Font("Consolas", 10, FontStyle.Bold);

            foreach (CoOpMate mate in activeCoOpMates)
            {
                DrawCharacterAnimated(
                    g,
                    humanRigs[mate.SkinIndex],
                    mate.X,
                    mate.Y,
                    PlayerSize,
                    mate.Angle,
                    mate.WalkPhase,
                    moving: mate.IsMoving,
                    sprinting: false,
                    zombieReachArms: false);

                SizeF mSz = g.MeasureString(mate.Name, tagFont);
                g.DrawString(mate.Name, tagFont, Brushes.Black, mate.X - mSz.Width / 2f + 1, mate.Y - PlayerSize / 2f - 17);
                g.DrawString(mate.Name, tagFont, Brushes.LimeGreen, mate.X - mSz.Width / 2f, mate.Y - PlayerSize / 2f - 18);
            }

            float drawPx = playerX - MathF.Cos(playerAngle) * gunRecoil;
            float drawPy = playerY - MathF.Sin(playerAngle) * gunRecoil;

            DrawCharacterAnimated(
                g,
                humanRigs[equippedSkin],
                drawPx,
                drawPy,
                PlayerSize,
                playerAngle,
                walkAnimPhase,
                moving: isMovingNow,
                sprinting: isSprintingNow,
                zombieReachArms: false);

            foreach (MapProp prop in mapProps)
            {
                float sway = MathF.Sin(prop.SwayPhase) * (prop.Kind == PropKind.Tree ? 2.2f : 1.5f);
                using var shadowBrush = new SolidBrush(Color.FromArgb(75, 8, 12, 8));
                using var pBrush = new SolidBrush(prop.PrimaryColor);
                using var sBrush = new SolidBrush(prop.SecondaryColor);

                switch (prop.Kind)
                {
                    case PropKind.Tree:
                        g.FillEllipse(shadowBrush, prop.X - prop.Radius + 6f, prop.Y - prop.Radius * 0.55f + 8f, prop.Radius * 2.1f, prop.Radius * 1.55f);
                        g.FillRectangle(Brushes.SaddleBrown, prop.X - 4.5f, prop.Y - 2f, 9f, 12f);
                        g.FillEllipse(pBrush, prop.X - prop.Radius + sway, prop.Y - prop.Radius * 1.15f, prop.Radius * 2f, prop.Radius * 1.85f);
                        g.FillEllipse(sBrush, prop.X - prop.Radius * 0.72f + sway * 1.1f, prop.Y - prop.Radius * 1.02f, prop.Radius * 1.3f, prop.Radius * 1.15f);
                        break;

                    case PropKind.Bush:
                        g.FillEllipse(shadowBrush, prop.X - prop.Radius + 4f, prop.Y - prop.Radius * 0.35f + 5f, prop.Radius * 2f, prop.Radius * 1.1f);
                        g.FillEllipse(pBrush, prop.X - prop.Radius + sway * 0.6f, prop.Y - prop.Radius * 0.75f, prop.Radius * 2f, prop.Radius * 1.4f);
                        g.FillEllipse(sBrush, prop.X - prop.Radius * 0.62f + sway * 0.6f, prop.Y - prop.Radius * 0.68f, prop.Radius * 1.15f, prop.Radius * 0.85f);
                        break;

                    case PropKind.Reed:
                        using (var reedPen = new Pen(pBrush, 2.5f))
                        {
                            for (int k = -1; k <= 1; k++)
                            {
                                float bx = prop.X + k * 5f;
                                g.DrawLine(reedPen, bx, prop.Y + 6f, bx + sway + k * 2f, prop.Y - prop.Radius - 4f);
                            }
                        }
                        break;

                    case PropKind.Rock:
                        g.FillEllipse(shadowBrush, prop.X - prop.Radius + 4f, prop.Y - prop.Radius * 0.35f + 5f, prop.Radius * 2f, prop.Radius);
                        g.FillEllipse(pBrush, prop.X - prop.Radius, prop.Y - prop.Radius * 0.7f, prop.Radius * 2f, prop.Radius * 1.3f);
                        g.FillEllipse(sBrush, prop.X - prop.Radius * 0.65f, prop.Y - prop.Radius * 0.62f, prop.Radius * 1.05f, prop.Radius * 0.7f);
                        break;
                }
            }

            string activeTag = string.IsNullOrWhiteSpace(gamerTag) ? T("Spieler", "Player") : gamerTag;
            SizeF tagSize = g.MeasureString(activeTag, tagFont);
            g.DrawString(activeTag, tagFont, Brushes.Black, playerX - tagSize.Width / 2f + 1, playerY - PlayerSize / 2f - 17);
            g.DrawString(activeTag, tagFont, Brushes.Cyan, playerX - tagSize.Width / 2f, playerY - PlayerSize / 2f - 18);

            if (state == GameState.Playing)
                DrawCustomCrosshairAt(g, mousePos.X, mousePos.Y);

            g.Restore(worldSaved);

            float lowHpPulse = playerHp > 0 && playerHp <= maxPlayerHp * 0.35f
                ? 0.14f + 0.08f * MathF.Sin(waterWavePhase * 1.4f)
                : 0f;
            float totalBloodAlpha = bloodEffects ? Math.Clamp(damageFlashAlpha + lowHpPulse, 0f, 0.85f) : 0f;

            if (totalBloodAlpha > 0.01f)
            {
                int a = (int)(totalBloodAlpha * 255);
                using var bloodWash = new SolidBrush(Color.FromArgb(a / 3, 165, 8, 8));
                g.FillRectangle(bloodWash, 0, 0, w, h);

                int borderThick = 34;
                using var edgeBlood = new SolidBrush(Color.FromArgb(a, 175, 10, 10));
                g.FillRectangle(edgeBlood, 0, 0, w, borderThick);
                g.FillRectangle(edgeBlood, 0, h - borderThick, w, borderThick);
                g.FillRectangle(edgeBlood, 0, borderThick, borderThick, h - borderThick * 2);
                g.FillRectangle(edgeBlood, w - borderThick, borderThick, borderThick, h - borderThick * 2);
            }

            int secsTotal = (int)runElapsedSeconds;
            string clock = $"{secsTotal / 60:00}:{secsTotal % 60:00}";
            string moveMode = dashTimer > 0
                ? "DASH"
                : (isSwimmingNow
                    ? T("SCHWIMMEN", "SWIMMING")
                    : (isSprintingNow ? T("RENNEN", "SPRINT") : (isMovingNow ? T("GEHEN", "WALK") : T("BEREIT", "IDLE"))));

            string ammoText = reloadTimer > 0
                ? T("LÄDT...", "RELOAD...")
                : $"{currentAmmo}/{GetWeaponMagCapacity(equippedWeapon)}";

            string roomSuffix = isMultiplayerMatchActive ? $"  |  MP [{currentRoomCode}]" : "";

            using var hudFont = new Font("Consolas", 11, FontStyle.Bold);
            g.DrawString($"[{activeTag}]{roomSuffix}  {T("Welle", "Wave")}: {wave}  |  {GetMapDisplayName(currentMap)}  |  {clock}  |  {moveMode}  |  {T("Munition", "Ammo")}: {ammoText}  |  Coins: {coins}",
                hudFont, Brushes.White, 16, 14);

            g.FillRectangle(Brushes.DarkRed, 16, 42, 190, 14);
            float hpRatio = Math.Clamp(playerHp / (float)Math.Max(1, maxPlayerHp), 0f, 1f);
            g.FillRectangle(Brushes.LimeGreen, 16, 42, (int)(190 * hpRatio), 14);
            g.DrawRectangle(Pens.White, 16, 42, 190, 14);

            using (var stamBgBrush = new SolidBrush(Color.FromArgb(35, 42, 58)))
            {
                g.FillRectangle(stamBgBrush, 216, 42, 150, 14);
            }

            float stamRatio = Math.Clamp(stamina / MaxStamina, 0f, 1f);
            Color stamColor = isExhausted ? Color.IndianRed : (isSprintingNow ? Color.Gold : Color.DeepSkyBlue);
            using (var stamBrush = new SolidBrush(stamColor))
            {
                g.FillRectangle(stamBrush, 216, 42, (int)(150 * stamRatio), 14);
            }

            g.DrawRectangle(Pens.White, 216, 42, 150, 14);

            using var hpFont = new Font("Consolas", 9, FontStyle.Bold);
            g.DrawString($"HP: {playerHp}/{maxPlayerHp}", hpFont, Brushes.White, 22, 42);
            g.DrawString(isExhausted ? T("ERSCHÖPFT", "EXHAUSTED") : $"SHIFT: {(int)stamina}%", hpFont, Brushes.White, 222, 42);

            if (mapBannerTicks > 0 && !string.IsNullOrEmpty(mapBannerText))
            {
                SizeF bSize = g.MeasureString(mapBannerText, hudFont);
                float bx = (w - bSize.Width) / 2f - 14;
                float by = 72;
                using var banBg = new SolidBrush(Color.FromArgb(190, 18, 22, 34));
                g.FillRectangle(banBg, bx, by, bSize.Width + 28, bSize.Height + 10);
                g.DrawRectangle(Pens.Gold, bx, by, bSize.Width + 28, bSize.Height + 10);
                g.DrawString(mapBannerText, hudFont, Brushes.Gold, bx + 14, by + 5);
            }

            if (mapTransitionProgress > 0f)
            {
                float cover = mapTransitionProgress <= 0.5f
                    ? mapTransitionProgress * 2f
                    : (1f - mapTransitionProgress) * 2f;
                int wipeW = (int)(w * Math.Clamp(cover, 0f, 1f));
                using var wipeBrush = new SolidBrush(Color.FromArgb(230, 10, 12, 18));
                g.FillRectangle(wipeBrush, 0, 0, wipeW, h);
            }
        }

        private static void DrawCharacterAnimated(
            Graphics g,
            CharacterRig? rig,
            float cx,
            float cy,
            int size,
            float facingAngleRad,
            float phase,
            bool moving,
            bool sprinting,
            bool zombieReachArms)
        {
            if (rig?.BodyAndHead == null || rig.LeftLeg == null || rig.RightLeg == null || rig.LeftArm == null || rig.RightArm == null)
                return;

            float swingAmp = moving ? (sprinting ? 8.5f : 5.5f) : 0f;
            float legSwing = moving ? MathF.Sin(phase) * swingAmp : 0f;
            float armSwing = moving ? -MathF.Sin(phase) * (swingAmp * 0.85f) : 0f;
            float bobY = moving ? MathF.Abs(MathF.Cos(phase)) * (sprinting ? -3.5f : -2.0f) : 0f;

            float scale = size / 16f;

            GraphicsState saved = g.Save();
            g.TranslateTransform(cx, cy + bobY);
            g.RotateTransform(facingAngleRad * (180f / MathF.PI) + 90f);
            g.ScaleTransform(scale, scale);

            using var groundShadow = new SolidBrush(Color.FromArgb(72, 6, 8, 12));
            g.FillEllipse(groundShadow, -6.5f, 5.5f, 13f, 5.5f);

            g.DrawImage(rig.LeftLeg, -3f, 5f + legSwing, 2f, 3f);
            g.DrawImage(rig.RightLeg, 1f, 5f - legSwing, 2f, 3f);
            g.DrawImage(rig.BodyAndHead, -6f, -6f, 12f, 10f);

            if (zombieReachArms)
            {
                g.DrawImage(rig.LeftArm, -7f, -4f + armSwing * 0.4f, 2f, 3f);
                g.DrawImage(rig.RightArm, 5f, -4f - armSwing * 0.4f, 2f, 3f);
            }
            else
            {
                g.DrawImage(rig.LeftArm, -7f, -2f + armSwing, 2f, 3f);
                g.DrawImage(rig.RightArm, 5f, -2f - armSwing, 2f, 3f);
            }

            g.Restore(saved);
        }

        private void DrawPauseOverlay(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            using var dim = new SolidBrush(Color.FromArgb(175, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, w, h);

            using var pFont = new Font("Impact", 40, FontStyle.Bold);
            using var btnFont = new Font("Consolas", 13, FontStyle.Bold);

            string pTitle = T("PAUSE", "PAUSED");
            SizeF tSz = g.MeasureString(pTitle, pFont);
            g.DrawString(pTitle, pFont, Brushes.White, (w - tSz.Width) / 2f, h / 2f - 65);

            using var resBg = new SolidBrush(Color.FromArgb(38, 165, 85));
            g.FillRectangle(resBg, pauseResumeRect);
            g.DrawRectangle(Pens.White, pauseResumeRect);
            string resTxt = T("WEITERSPIELEN (ESC)", "RESUME (ESC)");
            SizeF rSz = g.MeasureString(resTxt, btnFont);
            g.DrawString(resTxt, btnFont, Brushes.White,
                pauseResumeRect.X + (pauseResumeRect.Width - rSz.Width) / 2f,
                pauseResumeRect.Y + (pauseResumeRect.Height - rSz.Height) / 2f);

            using var menuBg = new SolidBrush(Color.FromArgb(45, 130, 230));
            g.FillRectangle(menuBg, pauseMenuRect);
            g.DrawRectangle(Pens.White, pauseMenuRect);
            string menuTxt = T("HAUPTMENÜ", "MAIN MENU");
            SizeF mSz = g.MeasureString(menuTxt, btnFont);
            g.DrawString(menuTxt, btnFont, Brushes.White,
                pauseMenuRect.X + (pauseMenuRect.Width - mSz.Width) / 2f,
                pauseMenuRect.Y + (pauseMenuRect.Height - mSz.Height) / 2f);
        }

        private void DrawTutorialOverlay(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            using var dim = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, w, h);

            int boxW = Math.Min(540, w - 80);
            int boxH = 260;
            int boxX = (w - boxW) / 2;
            int boxY = (h - boxH) / 2;
            Rectangle box = new Rectangle(boxX, boxY, boxW, boxH);

            using var bg = new SolidBrush(Color.FromArgb(24, 30, 48));
            using var border = new Pen(Color.Cyan, 3f);
            g.FillRectangle(bg, box);
            g.DrawRectangle(border, box);

            using var titleFont = new Font("Consolas", 15, FontStyle.Bold);
            using var bodyFont = new Font("Consolas", 11, FontStyle.Regular);
            using var btnFont = new Font("Consolas", 11, FontStyle.Bold);

            string[] titles = IsEnglish ? tutorialTitlesEng : tutorialTitlesDe;
            string[] texts = IsEnglish ? tutorialTextsEng : tutorialTextsDe;

            g.DrawString("TUTORIAL • " + titles[tutorialStep], titleFont, Brushes.Gold, boxX + 24, boxY + 22);
            g.DrawString(texts[tutorialStep], bodyFont, Brushes.White,
                new RectangleF(boxX + 24, boxY + 62, boxW - 48, 125));

            using var skipBg = new SolidBrush(Color.FromArgb(60, 65, 85));
            g.FillRectangle(skipBg, tutSkipRect);
            g.DrawRectangle(Pens.Gray, tutSkipRect);
            g.DrawString(T("Überspringen", "Skip"), btnFont, Brushes.White, tutSkipRect.X + 16, tutSkipRect.Y + 9);

            using var nextBg = new SolidBrush(Color.FromArgb(40, 160, 85));
            g.FillRectangle(nextBg, tutNextRect);
            g.DrawRectangle(Pens.White, tutNextRect);
            string nextText = tutorialStep < titles.Length - 1
                ? T("Weiter ->", "Next ->")
                : T("Los geht's!", "Let's Go!");
            g.DrawString(nextText, btnFont, Brushes.White, tutNextRect.X + 26, tutNextRect.Y + 9);
        }

        private void DrawGameOverOverlay(Graphics g)
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            using var dark = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.FillRectangle(dark, 0, 0, w, h);

            using var goFont = new Font("Impact", 46, FontStyle.Bold);
            using var infoFont = new Font("Consolas", 14, FontStyle.Bold);

            string activeTag = string.IsNullOrWhiteSpace(gamerTag) ? T("Spieler", "Player") : gamerTag;
            g.DrawString("WASTED / GAME OVER", goFont, Brushes.Crimson, w / 2f - 210, h / 2f - 85);
            g.DrawString($"{activeTag}  •  {T("Welle", "Wave")} {wave}  •  Coins: {coins}  •  {T("Punkte", "Score")}: {score}",
                infoFont, Brushes.White, w / 2f - 245, h / 2f - 15);
            g.DrawString(T("[R] / [LEERTASTE]: Neustart   |   [M]: Hauptmenü", "[R] / [SPACE]: Restart   |   [M]: Main Menu"),
                infoFont, Brushes.Gold, w / 2f - 220, h / 2f + 25);
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (state == GameState.Loading)
            {
                loadProgress = 100f;
                if (currentAccount != null)
                    state = GameState.MainMenu;
                else
                {
                    authFocusField = 0;
                    state = GameState.Auth;
                }
                SyncCombatCursorVisibility();
                UpdateLobbyMusicState();
                SyncDiscordPresence(force: true);
                return;
            }

            if (state == GameState.Paused)
            {
                if (pauseResumeRect.Contains(e.Location))
                {
                    state = GameState.Playing;
                    SyncCombatCursorVisibility();
                    SyncDiscordPresence(force: true);
                }
                else if (pauseMenuRect.Contains(e.Location))
                {
                    mouseDown = false;
                    isMultiplayerMatchActive = false;
                    activeCoOpMates.Clear();
                    audioMixer?.StopMovementLoop();
                    SaveGameData();
                    state = GameState.MainMenu;
                    SyncCombatCursorVisibility();
                    UpdateLobbyMusicState();
                    SyncDiscordPresence(force: true);
                }
                return;
            }

            if (state == GameState.Auth)
            {
                if (authModeLoginRect.Contains(e.Location))
                {
                    authMode = AuthMode.Login;
                    authMessage = "";
                    SyncDiscordPresence(force: true);
                }
                else if (authModeRegisterRect.Contains(e.Location))
                {
                    authMode = AuthMode.Register;
                    authMessage = "";
                    SyncDiscordPresence(force: true);
                }
                else if (authUserBoxRect.Contains(e.Location))
                {
                    authFocusField = 0;
                }
                else if (authPassBoxRect.Contains(e.Location))
                {
                    authFocusField = 1;
                }
                else if (authSubmitRect.Contains(e.Location))
                {
                    SubmitAuthForm();
                }
                return;
            }

            if (state == GameState.Tutorial)
            {
                if (tutSkipRect.Contains(e.Location))
                {
                    tutorialCompleted = true;
                    SaveGameData();
                    ResetPlayerAndWorld();
                    state = GameState.Playing;
                    SyncCombatCursorVisibility();
                    SyncDiscordPresence(force: true);
                }
                else if (tutNextRect.Contains(e.Location))
                {
                    if (tutorialStep < tutorialTitlesDe.Length - 1)
                    {
                        tutorialStep++;
                        SyncDiscordPresence(force: true);
                    }
                    else
                    {
                        tutorialCompleted = true;
                        SaveGameData();
                        ResetPlayerAndWorld();
                        state = GameState.Playing;
                        SyncCombatCursorVisibility();
                        SyncDiscordPresence(force: true);
                    }
                }
                return;
            }

            if (state == GameState.MainMenu)
            {
                if (mpView != MultiplayerView.None)
                {
                    if (mpCloseBtnRect.Contains(e.Location))
                    {
                        mpView = MultiplayerView.None;
                        return;
                    }

                    if (mpView == MultiplayerView.ModeSelect)
                    {
                        if (mpCreateLobbyBtnRect.Contains(e.Location))
                            CreateMultiplayerLobby();
                        else if (mpJoinByCodeBtnRect.Contains(e.Location))
                        {
                            mpStatusMessage = "";
                            mpView = MultiplayerView.JoinByCode;
                        }
                        return;
                    }

                    if (mpView == MultiplayerView.JoinByCode)
                    {
                        if (mpConfirmJoinBtnRect.Contains(e.Location))
                            JoinMultiplayerLobbyWithCode();
                        else if (mpBackToSelectBtnRect.Contains(e.Location))
                        {
                            mpStatusMessage = "";
                            mpView = MultiplayerView.ModeSelect;
                        }
                        return;
                    }

                    if (mpView == MultiplayerView.LobbyRoom)
                    {
                        if (mpCopyCodeBtnRect.Contains(e.Location))
                        {
                            try { Clipboard.SetText(currentRoomCode); } catch { }
                            mpStatusMessage = T($"Code {currentRoomCode} kopiert!", $"Code {currentRoomCode} copied!");
                            mpStatusIsError = false;
                        }
                        else if (mpStartMatchBtnRect.Contains(e.Location))
                            StartMultiplayerMatchFromLobby();
                        else if (mpLeaveLobbyBtnRect.Contains(e.Location))
                        {
                            lobbyPlayers.Clear();
                            currentRoomCode = "";
                            mpView = MultiplayerView.ModeSelect;
                        }
                        return;
                    }
                }

                if (logoutBtnRect.Contains(e.Location))
                {
                    LogoutToAuth();
                    return;
                }

                MainTab[] tabEnums =
                {
                    MainTab.Haupt,
                    MainTab.Skin,
                    MainTab.Waffenkammer,
                    MainTab.Karriere,
                    MainTab.Shop,
                    MainTab.Spind,
                    MainTab.Spin,
                    MainTab.BattlePass,
                    MainTab.Einstellung
                };
                for (int i = 0; i < 9; i++)
                {
                    if (mainTabRects[i].Contains(e.Location))
                    {
                        activeTab = tabEnums[i];
                        UpdateLobbyMusicState();
                        SyncDiscordPresence(force: true);
                        return;
                    }
                }

                if (activeTab == MainTab.Haupt)
                {
                    if (startBtnRect.Contains(e.Location))
                        StartFromMenu();
                    else if (multiplayerBtnRect.Contains(e.Location))
                        OpenMultiplayerMenu();
                }
                else if (activeTab == MainTab.Spind)
                {
                    SpindLockerTab[] stabs = { SpindLockerTab.Waffen, SpindLockerTab.Skins, SpindLockerTab.Lobbymusik, SpindLockerTab.Extras };
                    for (int i = 0; i < 4; i++)
                    {
                        if (spindLockerTabRects[i].Contains(e.Location))
                        {
                            activeSpindTab = stabs[i];
                            return;
                        }
                    }

                    if (activeSpindTab == SpindLockerTab.Waffen)
                    {
                        BuildCardsGrid(weaponNamesDe.Length, 188, 140, 2);
                        for (int i = 0; i < weaponNamesDe.Length; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;
                            if (ownedWeapons[i]) { equippedWeapon = i; SaveGameData(); SyncDiscordPresence(force: true); }
                            else if (coins >= weaponPrices[i]) { coins -= weaponPrices[i]; ownedWeapons[i] = true; equippedWeapon = i; SaveGameData(); SyncDiscordPresence(force: true); }
                        }
                    }
                    else if (activeSpindTab == SpindLockerTab.Skins)
                    {
                        BuildCardsGrid(skinNamesDe.Length, 188, 135, 3);
                        for (int i = 0; i < skinNamesDe.Length; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;
                            if (ownedSkins[i]) { equippedSkin = i; SaveGameData(); }
                            else if (coins >= skinPrices[i]) { coins -= skinPrices[i]; ownedSkins[i] = true; equippedSkin = i; SaveGameData(); }
                        }
                    }
                    else if (activeSpindTab == SpindLockerTab.Lobbymusik)
                    {
                        BuildCardsGrid(4, 188, 135, 2);
                        for (int i = 0; i < 4; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;
                            if (ownedLobbyMusic[i]) { selectedLobbyTrack = i; UpdateLobbyMusicState(); SaveGameData(); }
                            else if (coins >= lobbyTrackPrices[i]) { coins -= lobbyTrackPrices[i]; ownedLobbyMusic[i] = true; selectedLobbyTrack = i; UpdateLobbyMusicState(); SaveGameData(); }
                        }
                    }
                    else
                    {
                        BuildCardsGrid(3, 188, 145, 3);
                        for (int i = 0; i < 3; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;
                            if (i == 0)
                            {
                                int cost = GetWeaponMagUpgradeCost(equippedWeapon);
                                if (coins >= cost) { coins -= cost; weaponMagBonus[equippedWeapon]++; SaveGameData(); }
                            }
                            else if (i == 1)
                            {
                                if (coins >= 150) { coins -= 150; bonusArmorLevel++; SaveGameData(); }
                            }
                            else if (i == 2)
                            {
                                coins += 200;
                                audioMixer?.PlayOneShot(pickupClip, gain: 0.75f);
                                SaveGameData();
                            }
                        }
                    }
                }
                else if (activeTab == MainTab.Spin)
                {
                    if (spinBtnRect.Contains(e.Location))
                        StartSpin();
                }
                else if (activeTab == MainTab.BattlePass)
                {
                    BuildCardsGrid(battlePassRewards.Count, 190, 118, 4);
                    for (int i = 0; i < battlePassRewards.Count && i < currentCardRects.Count; i++)
                    {
                        if (currentCardRects[i].Contains(e.Location))
                        {
                            ClaimBattlePassReward(i);
                            return;
                        }
                    }
                }
                else if (activeTab == MainTab.Einstellung)
                {
                    SettingsCategory[] cats = { SettingsCategory.Grafik, SettingsCategory.Audio, SettingsCategory.Gameplay };
                    for (int i = 0; i < 3; i++)
                    {
                        if (settingsCatRects[i].Contains(e.Location))
                        {
                            activeSettingsCat = cats[i];
                            return;
                        }
                    }

                    if (activeSettingsCat == SettingsCategory.Grafik)
                    {
                        if (setBtnA.Contains(e.Location)) ToggleFullscreenF11();
                        else if (setBtnB.Contains(e.Location)) { vsyncEnabled = !vsyncEnabled; ApplyAudioAndSync(); SaveGameData(); }
                        else if (setBtnC.Contains(e.Location)) { crosshairStyleIndex = (crosshairStyleIndex + 1) % 4; SaveGameData(); }
                        else if (setBtnD.Contains(e.Location)) { crosshairColorIndex = (crosshairColorIndex + 1) % crosshairPalette.Length; SaveGameData(); }
                        else if (setBtnE.Contains(e.Location)) { crosshairSizeLevel = crosshairSizeLevel >= 3 ? 1 : crosshairSizeLevel + 1; SaveGameData(); }
                        else if (setBtnF.Contains(e.Location)) { bloodEffects = !bloodEffects; SaveGameData(); }
                    }
                    else if (activeSettingsCat == SettingsCategory.Audio)
                    {
                        if (setBtnA.Contains(e.Location)) { masterVolume = Math.Max(0f, MathF.Round(masterVolume - 0.1f, 2)); ApplyAudioAndSync(); SaveGameData(); }
                        else if (setBtnB.Contains(e.Location)) { masterVolume = Math.Min(1f, MathF.Round(masterVolume + 0.1f, 2)); ApplyAudioAndSync(); SaveGameData(); }
                        else if (setBtnC.Contains(e.Location)) { lobbyMusicEnabled = !lobbyMusicEnabled; UpdateLobbyMusicState(); SaveGameData(); }
                        else if (setBtnD.Contains(e.Location))
                        {
                            for (int k = 1; k <= 4; k++)
                            {
                                int cand = (selectedLobbyTrack + k) % 4;
                                if (ownedLobbyMusic[cand]) { selectedLobbyTrack = cand; break; }
                            }
                            UpdateLobbyMusicState();
                            SaveGameData();
                        }
                    }
                    else
                    {
                        if (setBtnA.Contains(e.Location)) { language = "DE"; SaveGameData(); SyncDiscordPresence(force: true); }
                        else if (setBtnB.Contains(e.Location)) { language = "ENG"; SaveGameData(); SyncDiscordPresence(force: true); }
                        else if (setBtnC.Contains(e.Location)) { autoReload = !autoReload; SaveGameData(); }
                        else if (setBtnD.Contains(e.Location)) { tutorialCompleted = false; SaveGameData(); }
                    }
                }
                else if (activeTab == MainTab.Skin)
                {
                    BuildCardsGrid(skinNamesDe.Length, 176, 155, 3);
                    for (int i = 0; i < currentCardRects.Count; i++)
                    {
                        if (currentCardRects[i].Contains(e.Location) && ownedSkins[i])
                        {
                            equippedSkin = i;
                            SaveGameData();
                            return;
                        }
                    }
                }
                else if (activeTab == MainTab.Waffenkammer)
                {
                    BuildCardsGrid(weaponNamesDe.Length, 176, 155, 2);
                    for (int i = 0; i < currentCardRects.Count; i++)
                    {
                        if (!currentCardRects[i].Contains(e.Location)) continue;

                        if (e.Y > currentCardRects[i].Y + 95 && i < gearNamesDe.Length && ownedGear[i])
                            equippedGear = i;
                        else if (ownedWeapons[i])
                            equippedWeapon = i;

                        SaveGameData();
                        SyncDiscordPresence(force: true);
                        return;
                    }
                }
                else if (activeTab == MainTab.Shop)
                {
                    ShopSubTab[] subEnums = { ShopSubTab.Upgrades, ShopSubTab.Skins, ShopSubTab.Waffen, ShopSubTab.Ausruestung };
                    for (int i = 0; i < 4; i++)
                    {
                        if (shopSubTabRects[i].Contains(e.Location))
                        {
                            activeShopTab = subEnums[i];
                            return;
                        }
                    }

                    if (activeShopTab == ShopSubTab.Upgrades)
                    {
                        BuildCardsGrid(5, 186, 130, 3);
                        for (int i = 0; i < 5 && i < currentCardRects.Count; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;
                            int cost = i switch
                            {
                                0 => GetUpgradeCost(fireRateLevel),
                                1 => GetUpgradeCost(speedLevel),
                                2 => GetUpgradeCost(maxHpLevel),
                                3 => GetUpgradeCost(staminaRegenLevel),
                                _ => GetUpgradeCost(coinBoostLevel)
                            };
                            if (coins < cost) return;
                            coins -= cost;
                            if (i == 0) fireRateLevel++;
                            else if (i == 1) speedLevel++;
                            else if (i == 2) maxHpLevel++;
                            else if (i == 3) staminaRegenLevel++;
                            else coinBoostLevel++;
                            SaveGameData();
                            return;
                        }
                    }
                    else if (activeShopTab == ShopSubTab.Skins)
                    {
                        BuildCardsGrid(skinNamesDe.Length, 186, 148, 3);
                        for (int i = 0; i < skinNamesDe.Length; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;
                            if (ownedSkins[i])
                            {
                                equippedSkin = i;
                                SaveGameData();
                            }
                            else if (coins >= skinPrices[i])
                            {
                                coins -= skinPrices[i];
                                ownedSkins[i] = true;
                                equippedSkin = i;
                                SaveGameData();
                            }
                            return;
                        }
                    }
                    else if (activeShopTab == ShopSubTab.Waffen)
                    {
                        BuildCardsGrid(weaponNamesDe.Length, 186, 162, 2);
                        for (int i = 0; i < weaponNamesDe.Length; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;

                            if (!ownedWeapons[i])
                            {
                                if (coins >= weaponPrices[i])
                                {
                                    coins -= weaponPrices[i];
                                    ownedWeapons[i] = true;
                                    equippedWeapon = i;
                                    SaveGameData();
                                    SyncDiscordPresence(force: true);
                                }
                            }
                            else
                            {
                                int midX = currentCardRects[i].X + currentCardRects[i].Width / 2;
                                if (e.Y > currentCardRects[i].Y + 95 && e.X >= midX)
                                {
                                    int mCost = GetWeaponMagUpgradeCost(i);
                                    if (coins >= mCost)
                                    {
                                        coins -= mCost;
                                        weaponMagBonus[i]++;
                                        equippedWeapon = i;
                                        SaveGameData();
                                    }
                                }
                                else if (e.Y > currentCardRects[i].Y + 95 && e.X < midX)
                                {
                                    int upgCost = GetWeaponUpgradeCost(i);
                                    if (coins >= upgCost)
                                    {
                                        coins -= upgCost;
                                        weaponLevels[i]++;
                                        equippedWeapon = i;
                                        SaveGameData();
                                        SyncDiscordPresence(force: true);
                                    }
                                }
                                else
                                {
                                    equippedWeapon = i;
                                    SaveGameData();
                                    SyncDiscordPresence(force: true);
                                }
                            }
                            return;
                        }
                    }
                    else if (activeShopTab == ShopSubTab.Ausruestung)
                    {
                        BuildCardsGrid(gearNamesDe.Length, 186, 155, 2);
                        for (int i = 0; i < gearNamesDe.Length; i++)
                        {
                            if (!currentCardRects[i].Contains(e.Location)) continue;

                            if (e.Y > currentCardRects[i].Y + 98)
                            {
                                if (coins >= 150)
                                {
                                    coins -= 150;
                                    bonusArmorLevel++;
                                    SaveGameData();
                                }
                                return;
                            }

                            if (ownedGear[i])
                            {
                                equippedGear = i;
                                SaveGameData();
                            }
                            else if (coins >= gearPrices[i])
                            {
                                coins -= gearPrices[i];
                                ownedGear[i] = true;
                                equippedGear = i;
                                SaveGameData();
                            }
                            return;
                        }
                    }
                }
                return;
            }

            if (state == GameState.Playing)
            {
                mousePos = e.Location;
                mouseDown = true;
            }
        }

        private void StartFromMenu()
        {
            if (currentAccount == null)
            {
                state = GameState.Auth;
                SyncCombatCursorVisibility();
                return;
            }

            isMultiplayerMatchActive = false;
            activeCoOpMates.Clear();
            SaveGameData();

            if (!tutorialCompleted)
            {
                tutorialStep = 0;
                ResetPlayerAndWorld();
                state = GameState.Tutorial;
            }
            else
            {
                ResetPlayerAndWorld();
                state = GameState.Playing;
            }
            SyncCombatCursorVisibility();
            SyncDiscordPresence(force: true);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                ToggleFullscreenF11();
                e.Handled = true;
                return;
            }

            if (state == GameState.Loading && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                loadProgress = 100f;
                if (currentAccount != null)
                    state = GameState.MainMenu;
                else
                {
                    authFocusField = 0;
                    state = GameState.Auth;
                }
                SyncCombatCursorVisibility();
                UpdateLobbyMusicState();
                SyncDiscordPresence(force: true);
                return;
            }

            if (state == GameState.Auth)
            {
                if (e.KeyCode == Keys.Tab)
                {
                    authFocusField = authFocusField == 0 ? 1 : 0;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Back)
                {
                    if (authFocusField == 0 && authUsername.Length > 0)
                        authUsername = authUsername[..^1];
                    else if (authFocusField == 1 && authPassword.Length > 0)
                        authPassword = authPassword[..^1];
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    SubmitAuthForm();
                }
                return;
            }

            if (state == GameState.MainMenu && mpView == MultiplayerView.JoinByCode)
            {
                if (e.KeyCode == Keys.Back && joinCodeInput.Length > 0)
                    joinCodeInput = joinCodeInput[..^1];
                else if (e.KeyCode == Keys.Enter)
                    JoinMultiplayerLobbyWithCode();
                else if (e.KeyCode == Keys.Escape)
                    mpView = MultiplayerView.None;
                return;
            }

            if (state == GameState.MainMenu && mpView != MultiplayerView.None && e.KeyCode == Keys.Escape)
            {
                mpView = MultiplayerView.None;
                return;
            }

            if (state == GameState.MainMenu && activeTab == MainTab.Haupt && e.KeyCode == Keys.Enter && mpView == MultiplayerView.None)
            {
                StartFromMenu();
                return;
            }

            if (state == GameState.Playing)
            {
                if (e.KeyCode == Keys.R)
                    StartReload();
                else if (e.KeyCode == Keys.Escape)
                {
                    mouseDown = false;
                    audioMixer?.StopMovementLoop();
                    state = GameState.Paused;
                    SyncCombatCursorVisibility();
                    SyncDiscordPresence(force: true);
                }
                return;
            }

            if (state == GameState.Paused && e.KeyCode == Keys.Escape)
            {
                state = GameState.Playing;
                SyncCombatCursorVisibility();
                SyncDiscordPresence(force: true);
                return;
            }

            if (state == GameState.GameOver)
            {
                if (e.KeyCode == Keys.R || e.KeyCode == Keys.Space)
                {
                    ResetPlayerAndWorld();
                    if (isMultiplayerMatchActive) SpawnCoOpMatesFromLobby();
                    state = GameState.Playing;
                    SyncCombatCursorVisibility();
                    SyncDiscordPresence(force: true);
                }
                else if (e.KeyCode == Keys.M)
                {
                    isMultiplayerMatchActive = false;
                    activeCoOpMates.Clear();
                    audioMixer?.StopMovementLoop();
                    state = GameState.MainMenu;
                    SyncCombatCursorVisibility();
                    UpdateLobbyMusicState();
                    SyncDiscordPresence(force: true);
                }
            }
        }

        private void OnKeyPress(object? sender, KeyPressEventArgs e)
        {
            if (state == GameState.Auth)
            {
                if (!char.IsControl(e.KeyChar))
                {
                    if (authFocusField == 0 && authUsername.Length < 16)
                        authUsername += e.KeyChar;
                    else if (authFocusField == 1 && authPassword.Length < 24)
                        authPassword += e.KeyChar;
                }
                return;
            }

            if (state == GameState.MainMenu && mpView == MultiplayerView.JoinByCode)
            {
                if (!char.IsControl(e.KeyChar) && char.IsLetterOrDigit(e.KeyChar) && joinCodeInput.Length < 8)
                    joinCodeInput += char.ToUpperInvariant(e.KeyChar);
                return;
            }

            if (state == GameState.MainMenu && editingTag && activeTab == MainTab.Haupt)
            {
                if (!char.IsControl(e.KeyChar) && gamerTag.Length < 16)
                {
                    gamerTag += e.KeyChar;
                    tagWarning = "";
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsKeyPressed(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CleanupGameResources();
            base.OnFormClosed(e);
        }

        private void CleanupGameResources()
        {
            if (resourcesCleanedUp) return;
            resourcesCleanedUp = true;

            SetCombatCursorHidden(false);
            SaveGameData();
            loopTimer.Stop();
            loopTimer.Dispose();
            audioMixer?.Dispose();
            discordRpc?.Dispose();
            for (int i = 0; i < humanRigs.Length; i++)
                humanRigs[i]?.Dispose();
            zombieRig?.Dispose();
        }
    }
}