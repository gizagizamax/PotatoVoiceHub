namespace PotatoVoiceHub
{
    public class VoiceHubOption
    {
        public string saveAudioPath;
        public string httpPort;
        public string saveAudioEncode;
        public string isClipboardPlay;
        public string isClipboardSaveAudio;

        /// <summary>A.I.VOICE2でセリフが反映されるまでの最大待ち時間(ms)。
        /// 固定待ちではなく、この時間まで反映をポーリングする上限として使う。
        /// 設定ファイルの互換のため名前はSendKeys時代のまま残している。</summary>
        public string aiv2SendKeysSleep;

        /// <summary>A.I.VOICE2でセリフを消すときに、文字数に上乗せして送る削除キーの数。</summary>
        public string aiv2DelCount;

        /// <summary>A.I.VOICE2で句読点を残したまま、文ごとに再生するかどうか。</summary>
        public string aiv2KeepPunctuation;
    }
}
