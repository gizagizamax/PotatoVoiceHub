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



## Recotte Studio と連携する

プロジェクトの編集画面
→メニューの[ファイル]
→[環境設定]
→[ユーザー定義音声連携の設定]を開きます

[＋]ボタンを押して下記設定をします
音声連携名：適当な名前
連携方法：コメントごとにコマンドを実行
実行コマンド：C:\Windows\System32\cmd.exe
引数：/C curl -G "http://localhost:2119/saveWave" --data-urlencode "text=%c" --data-urlencode "filePath=%o" --data-urlencode "presetName=%s"
拡張子：wav

[適応]を押してプロジェクトの編集画面まで戻ります。
→タイムラインにある[話者１]の設定を開きます([レイヤーのプロパティ]画面が開く)
→[音声連携]を先ほど適当な名前をつけたやつに変更します。
→[話者名]にA.I.Voice Editorのプリセット名を指定します。
  [話者名]を指定しないと、A.I.Voice Editorで選択しているキャラになります。
→[OK]でプロジェクトの編集画面へ。

[話者１]にコメントを記載します。
→[話者１]に更新マークが出てくるので押します。
→AIVoiceと連携して、コメントに音声がつきます。
※あらかじめ、PotateVoiceHub経由でAIVoice Editorを起動しておいてください。



# リリースノート

## PotatoVoiceHub_v2022.01.19

Plugin_PotatoVoiceHub

  ・Recotte Studioと連携時に[話者名]を指定できるようにしました。


## PotatoVoiceHub_v2022.01.16

Plugin_PotatoVoiceHub

  ・「棒読みちゃんの辞書変換を使う」チェックボックスを追加

  ・棒読みちゃんコマンドは読み上げないように修正。

PotetoVoiceHub

  ・テーマの切り替え機能を削除

  最新のAIVoice Editorに機能が追加されていたため。

  ・お試し再生の機能を削除

  元々デバッグ用に作った機能で、機能が充実してきたので邪魔なので消しました。

  ・音声保存APIを追加

  Recotte StudioからShift-Jisで送られてくるようなので、エンコードを指定できるようにしました。

  ・クリップボード連携を追加

  文字をコピーしたら、AIVoiceで勝手に再生したり、ファイルに保存したりします。

## PotatoVoiceHub_v2021.11.09

VoiceroidTalkPlus のランダム再生を使って「AIVOICE」→「VOICEROID・ガイノイド」の順に発話すると

AIVOICEとVOICEROIDが同時に発話していたのを修正

## PotatoVoiceHub_v2021.11.08

VoiceroidTalkPlusReceiverHub.exe を追加

A.I.VOICE Editor を起動しないで、ダークモード/ホワイトモードを設定すると落ちてたので修正

ログが自動でスクロールしなかったので修正

アプリのアイコンを追加

## vPotatoVoiceHub_v2021.10.29

初回版
