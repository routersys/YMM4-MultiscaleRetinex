# v1.0.0 - 局所トーン for YMM4

YukkuriMovieMaker4向けの局所トーンエフェクトプラグインの初回リリースです。
Jobson・Rahman・Woodellのマルチスケールレティネックス（IEEE Transactions on Image Processing、1997年）で映像の照明成分と反射成分を分解し、逆光や霞や色かぶりを局所的に補正します。
補正は入力とパラメータから決定論的に決まり、同じ設定では常に同じ出力になります。
計算はComputeSharpの計算シェーダーがDirect3D 12で実行し、YMM4のDirect3D 11側とは共有テクスチャおよび共有フェンスで接続します。
8言語のリソース構成のUIを備えます。

---

## 新機能

### 1. マルチスケールレティネックスの計算パイプライン

`MultiscaleRetinexPipeline`は、ハッシュ、レティネックス、描画の3段階の計算シェーダーを`ComputeContext`へ記録して実行します。バッファーは素材の大きさに応じて確保し、サイズが変わらないフレームでは再利用します。処理の流れは次のとおりです。

1. `HashBoundsShader`が、全画素のハッシュの総和とXORを集計し、不透明画素のバウンディングボックスを求めます。
2. `LinearizeShader`が、プリマルチプライドの入力を不透明度付きの4成分として縮小階層の最上段へ書き込みます。
3. `DownsampleShader`が、2×2平均の縮小階層を最大スケールに必要な深さまで構築します。
4. `BlurHorizontalShader`と`BlurVerticalShader`が、スケールごとに選んだ階層の上で正規化ガウシアンの分離可能畳み込みを実行します。
5. `AccumulateRetinexShader`が、画素値とサラウンドの対数差を等しい重みで反射成分バッファーへ合算します。
6. `RenderShader`が、色再現係数とゲインを適用して出力を描画します。

| シェーダー | 役割 |
|---|---|
| `HashBoundsShader` | 画素ハッシュの集計と不透明範囲の算出 |
| `LinearizeShader` | 入力の縮小階層最上段への展開 |
| `DownsampleShader` | 縮小階層の構築 |
| `BlurHorizontalShader` / `BlurVerticalShader` | ガウシアンサラウンドの分離可能畳み込み |
| `AccumulateRetinexShader` | 対数差の合算 |
| `RenderShader` | 色再現とゲインの適用 |

### 2. 論文に基づく定式

補正の計算は、Jobson・Rahman・Woodellの論文「A Multiscale Retinex for Bridging the Gap Between Color Images and the Human Observation of Scenes」（1997年）のマルチスケールレティネックスに基づきます。各画素の反射成分は、合計が1になるように正規化したガウシアンサラウンドとの対数差を、複数のスケールにわたり等しい重みで合算した値です。素材の縁と透明部分は不透明度で重み付けし、照明の推定から除きます。

サラウンドの標準偏差は、局所スケールと広域スケールの2値の間へ等比で配置し、素材の長辺に対する割合で指定します。既定値の3%と49%は、原論文が512画素程度の画像へ用いた15画素と250画素の比率に対応します。

- 色恒常モード: 3つの色チャンネルを独立に補正します。灰色化を補う色再現係数は、原論文の係数を`log(1+x)`の形で正の値に保ち、無彩色の画素で1になるように正規化します。非線形の強さは原論文の`α = 125`です。
- 彩度保持モード: 輝度への単一のレティネックスと色比率の保存で構成し、Petro・Sbert・Morelの解説論文「Multiscale Retinex」（Image Processing On Line、2014年）の輝度処理方式に対応します。
- 表示への写像: Barnard・Funtの論文「Investigations into Multi-Scale Retinex」（1999年）が示す、中間値を中間へ写す画像非依存の単一パラメータのゲインです。フレームごとのヒストグラムを使用しないため、映像で明滅が発生しません。

広いサラウンドの畳み込みは、標準偏差が品質の基準シグマ以上に保たれる深さまで縮小した階層の上で実行し、双線形補間で全解像度へ戻します。カーネル半径は標準偏差の3倍です。乱数と時間値は使用せず、すべての値が入力から決定論的に決まります。

### 3. 出力矩形の最小化

ハッシュ段階で求めた不透明画素のバウンディングボックスだけを描画します。

- 矩形は4画素境界へそろえ、素材の範囲へクランプします。
- 出力テクスチャは矩形の大きさで確保し、Direct2Dの`Crop`と`AffineTransform2D`で元の位置へ合成します。
- 補正は素材の外側へ広がらないため、出力範囲は入力の不透明範囲と一致します。

### 4. 構造キャッシュ

反射成分を決める入力が変わらないフレームでは、レティネックスの計算を再利用します。

- 素材の内容は、全画素のハッシュの総和とXORの2値へ集約し、8個の整数の読み戻しで前フレームと比較します。
- 画素のハッシュ、素材の大きさ、品質、処理モード、局所スケール、広域スケールが一致する場合は、レティネックス段階を実行しません。
- 構造が同じで、ダイナミックレンジ、明るさ、色再現、出力矩形も変わらないフレームでは、描画段階も実行せず、前フレームの出力テクスチャを使用します。

### 5. Direct3D 11・Direct3D 12相互運用

`MultiscaleRetinexGpuInterop`は、YMM4のDirect3D 11・Direct2D側と、ComputeSharpのDirect3D 12側を接続します。ComputeSharpの`GraphicsDevice`は、YMM4が使うDXGIアダプターのLUIDと一致するものを選びます。

入力と出力は、ComputeSharpで確保した共有テクスチャをDirect3D 11のテクスチャとして開き、Direct2Dのビットマップとして扱います。入力のテクスチャは素材の大きさで確保し、出力のテクスチャは不透明範囲の矩形を収める容量で確保して拡大時だけ作り直します。両デバイスの同期は、Direct3D 12のフェンスを共有フェンスとしてDirect3D 11側で開いて行います。

`BeginCompute`は、Direct3D 11のコマンドを送出したうえでDirect3D 12側を待機させ、`EndCompute`は、Direct3D 12側の完了をDirect3D 11側で待ちます。

Direct3D 12デバイスの取得や共有リソースの作成に失敗した場合は、`TryCreate`が`null`を返し、エフェクトを適用せず入力映像を表示します。

### 6. カスタムシェーダーによる合成

`MultiscaleRetinexCustomEffect`は、`[CustomEffect(2)]`の2入力エフェクトです。入力0は元映像、入力1は補正結果です。ピクセルシェーダー`MultiscaleRetinex.hlsl`の`main`は、`amount`が0以下のとき元映像をそのまま返し、そうでないときは補正結果のRGBをアルファでクランプし、`source + (result - source) * amount`の線形補間で元映像と補正結果を合成します。補正結果の不透明度は入力と一致するため、合成は単純な補間です。

定数バッファーは`Amount`と3つの詰め物で16バイトです。`MapInputRectsToOutputRect`は2つの入力矩形の和集合を出力矩形とします。補正は素材の外側へ広がらないため、出力範囲は素材と一致します。

シェーダーリソース: `pack://application:,,,/MultiscaleRetinex;component/Shaders/MultiscaleRetinex.cso`（ps_5_0、`ShaderResourceUri.Get`が生成）

### 7. エフェクト定義とパラメータ

`MultiscaleRetinexEffect`は、YMM4の映像エフェクトとして宣言されます。

`[VideoEffect]`属性は以下のパラメーターで宣言されます。

- 表示名: `Texts.MultiscaleRetinex`（ローカライズキー、日本語では「局所トーン」）
- カテゴリー: `VideoEffectCategories.Filtering`
- 検索タグ: `TagBacklight`・`TagHaze`・`TagColorConstancy`
- `IsAviUtlSupported = false`によりAviUtl向けEXO出力は非対応
- `ResourceType = typeof(Texts)`でローカライズリソースを指定

公開プロパティは以下のとおりです。基本項目は「基本」グループ、スケール項目は「スケール」グループ、調整項目は「調整」グループに属します。

| プロパティ | 型 | デフォルト | 内部範囲 | アニメーション |
|---|---|---|---|---|
| `Amount` | `Animation` | 100 | 0〜100 | あり |
| `Quality` | `MultiscaleRetinexQuality` | `High` | — | なし |
| `Mode` | `MultiscaleRetinexMode` | `ColorConstancy` | — | なし |
| `LocalScale` | `Animation` | 3 | 0.1〜50 | あり |
| `GlobalScale` | `Animation` | 49 | 1〜200 | あり |
| `Contrast` | `Animation` | 30 | 0〜100 | あり |
| `Brightness` | `Animation` | 0 | -100〜100 | あり |
| `ColorRestoration` | `Animation` | 100 | 0〜100 | あり |

`GetAnimatables`は`Amount`・`LocalScale`・`GlobalScale`・`Contrast`・`Brightness`・`ColorRestoration`を返します。

`CreateExoVideoFilters`は空のシーケンスを返します（EXO非対応）。`CreateVideoEffect`は映像処理用のインスタンスを生成します。エフェクトを最初に生成したときに、更新確認を一度だけ開始します。

### 8. フレームごとの更新

各フレームでYMM4の`EffectDescription`からフレーム位置、アイテム長、FPSを取得し、アニメーション値を評価します。値をパイプラインが前提とする範囲へ制限してから転送します。

| パラメータ | 変換 |
|---|---|
| `Amount` | `value / 100` をカスタムシェーダーの`Amount`へ |
| `LocalScale` | `value / 100` を0.001〜0.5へクランプし、素材の長辺に掛けて標準偏差の画素数へ |
| `GlobalScale` | `value / 100` を0.01〜2へクランプし、素材の長辺に掛けて標準偏差の画素数へ |
| `Contrast` | `value / 100` を0〜1へクランプし、0.1〜1.5のゲインへ |
| `Brightness` | `value / 100` を-1〜1へクランプし、±0.5のオフセットへ |
| `ColorRestoration` | `value / 100` を0〜1へクランプ |

強さが0以下のときは、補正を行わず入力映像をそのまま出力します。入力の範囲が有限でない場合や、素材が長辺8192画素または総画素数16777216画素を超える場合も、入力映像を表示します。

### 9. 品質設定

品質は、サラウンドのスケール数と、畳み込みを実行する縮小階層の基準シグマをまとめて切り替えます。

| 品質 | スケール数 | 基準シグマ |
|---|---:|---:|
| 標準 | 3 | 4画素 |
| 高品質 | 4 | 6画素 |
| 最高品質 | 5 | 8画素 |

スケール数は照明推定の広さの段階数です。原論文は3スケール以上を推奨しています。基準シグマは縮小階層の上で実行する畳み込みの標準偏差の下限で、大きいほど縮小が浅くなり推定が細かくなります。

### 10. ローカライズ

`Texts`クラスは`[AutoGenLocalizer]`属性を持つ`partial`クラスとして宣言されます。
`YukkuriMovieMaker.Generator`のソースジェネレーターが`Texts.csv`を処理し、各ロケールのリソースファイルを自動生成します。

対応リソース: 日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

主なローカライズキーは以下のとおりです。

| キー | ja-jp |
|---|---|
| `MultiscaleRetinex` | 局所トーン |
| `BasicGroup` | 基本 |
| `ScaleGroup` | スケール |
| `AdjustGroup` | 調整 |
| `Amount` | 強さ |
| `Quality` | 品質 |
| `Mode` | 処理モード |
| `ModeColorConstancy` | 色恒常 |
| `ModeChromaticity` | 彩度保持 |
| `LocalScale` | 局所スケール |
| `GlobalScale` | 広域スケール |
| `Contrast` | ダイナミックレンジ |
| `Brightness` | 明るさ |
| `ColorRestoration` | 色再現 |
| `QualityBalanced` | 標準 |
| `QualityHigh` | 高品質 |
| `QualityUltra` | 最高品質 |
| `TagBacklight` | 逆光 |
| `TagHaze` | 霞 |
| `TagColorConstancy` | 色恒常 |
| `UpdateAvailableMessage` | 新しいバージョン {0} が公開されています。 |
