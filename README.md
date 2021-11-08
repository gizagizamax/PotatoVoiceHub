# PotatoVoiceHub

棒読みちゃんの音声をA.I.Voiceに変えるアプリ


# 使い方

1.Plugin_PotatoVoiceHub.dll を棒読みちゃん(BouyomiChan.exe)のあるフォルダへコピーします。

2.棒読みちゃんを起動して、PluginPotatoVoiceHub を有効にします。

  棒読みちゃん起動時に有効にするダイアログが表示されます。

  棒読みちゃんの[その他]タブから有効にすることもできます。

3.PotatoVoiceHub.exe を実行します。

4.[A.I.VOICE開始]をクリックするとA.I.VOICE Editorが起動します。

5.棒読みちゃんの音声がA.I.Voiceに変わります。



# PotatoVoiceHubの設定

## AIVoiceEditor.exeの場所

A.I.Voiceをインストールしたフォルダにあります。

レジストリから設定されますが、されていない場合は手動で設定してください。



## 受信HTTPポート

ここで指定したポートをめがけて、棒読みちゃんからテキストが飛んできます。

デフォルトでは 2119番ポートを使用しますが、他のアプリで使っている場合は適当な数字に変えてください。

棒読みちゃんのツールバーにある PluginPotatoVoiceHub を開き、こちらのポートも合わせてください。



## Voiceroid Talk Plus と連携する

@Wangdora氏作成の Voiceroid Talk Plus を PotatoVoiceHub に対応させる事ができます(非公式)。

PotatoVoiceHub.exe と VoiceroidTalkPlusReceiverHub.exe を実行してください。

Plugin_PotatoVoiceHub.dll は不要なので、棒読みちゃんのプラグイン設定でOFFにしておいてください。

基本的にはこれだけで、いつも通りVoiceroid Talk Plus が 最新の A.I.VOICE で動くようになります。

PotatoVoiceHub と VoiceroidTalkPlusReceiverHub のポートの数字は同じ物を入れておいてください。



# リリースノート

## PotatoVoiceHub_v2021.11.09

VoiceroidTalkPlus のランダム再生を使って「AIVOICE」→「VOICEROID・ガイノイド」の順に発話すると
AIVOICEとVOICEROIDが同時に発話していたのを修正(VOICEROID持ってないので未確認。報告求)

## PotatoVoiceHub_v2021.11.08

VoiceroidTalkPlusReceiverHub.exe を追加

A.I.VOICE Editor を起動しないで、ダークモード/ホワイトモードを設定すると落ちてたので修正

ログが自動でスクロールしなかったので修正

アプリのアイコンを追加

## vPotatoVoiceHub_v2021.10.29

初回版
