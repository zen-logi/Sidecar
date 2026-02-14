---
description: プロジェクトコーディングルール
---

# プロジェクトルール

## 回答言語

- 回答は必ずすべて日本語で行う

## 実装スタイル

- Serviceは必ずInterfaceを継承しDIを用いる
- コードはMVC+Serviceを遵守する
- Microsoftおよび.NETの推奨する設計・実装を基本とする
- .NETを使用する場合は.NET10以降を利用する
- 実装は常にモダナイズを意識する
- editorconfigやappsettingsも適宜設定する
- プライマリコンストラクタを適宜使用する
- プライマリコンストラクタのパラメータを直接使用する
- editorconfigを参照した実装を基本とする

## ドキュメント・コメント

- コード内のSummaryは必ず記述し、日本語で「ですます」はやめる。また末尾に句点はつけない
- 継承先のService内のpublic methodは `inheritdoc` とし、private methodはSummaryを記述する
- コメントおよびXAMLは日本語で記述する。ただしコンソール出力はその限りではない