# PBZ Mod Manager

**[한국어](#한국어) · [English](#english) · [简体中文](#简体中文) · [日本語](#日本語)**

Phantom Blade Zero (Steam) · Windows · GPL-3.0 · Developed with [Claude](https://claude.com/claude-code) (Anthropic)

---

## 한국어

Phantom Blade Zero(팬텀 블레이드 제로)용 모드 매니저입니다. 받은 모드 압축 파일을 게임 폴더의 알맞은 위치에 설치하고, 무엇을 설치했는지 기록해 두었다가 그대로 되돌릴 수 있게 해 줍니다.

> - **Steam 버전 기준**으로 만든 프로그램입니다. 게임 폴더 구조와 실행 파일(`platform\steam\PBZ.exe`)을 Steam 버전에 맞춰 다룹니다.
> - 비공식 커뮤니티 도구입니다. 게임 제작사나 Nexus Mods와 관련이 없습니다.

### 주요 기능

- **안전한 설치와 제거**: 설치할 때 덮어쓰는 파일을 모두 백업하고, 제거하면 원래 상태로 되돌립니다. 설치 후 바뀐 파일이나 모드가 만든 파일은 지우지 않고 보관 폴더로 옮깁니다.
- **미리보기와 파일 고르기**: 설치 전에 어떤 파일이 어디로 가는지 경로별·작업별로 보여 주고, 원하지 않는 파일은 체크를 풀어 뺄 수 있습니다. 뺀 파일은 다음 업데이트에도 그대로 빠집니다.
- **알아서 맞는 자리에 설치**
  - JS 모드: PBZModLoader(`ProjectPBZ/PBZModLoader/mods`)와 Mods Menu(`.../JavaScript/PBZModsMenu/Mods`) 중 쓰는 쪽에 맞춰 배치
  - UE4SS 본체는 `platform/steam`, UE4SS Lua 모드는 `platform/steam/ue4ss/Mods`
  - pak 모드는 `Content/Paks/~mods`, 블루프린트 모드는 `Content/Paks/LogicMods`
  - "(choose one)"처럼 같은 파일을 쓰는 옵션 폴더는 묶음마다 하나씩 골라 설치
- **Nexus 정보 자동 입력**: 모드 주소를 넣으면 Nexus 공개 API로 카테고리와 설명의 설치 방법을 가져옵니다.
- **최신 버전 확인**: Nexus Mods와 GitHub 릴리스에서 새 버전이 있는지 확인하고, 다운로드 페이지를 열어 줍니다.
- **버전 관리**: 같은 모드의 여러 버전을 한 모드로 묶어 업데이트·다운그레이드하고, 이전 버전 파일을 정리(휴지통으로)할 수 있습니다.
- **정리 도구**: 설치된 모드 순서 바꾸기와 이름 붙인 구분선, 한 줄 메모, 여러 모드 한 번에 설치·제거
- **미리보기 이미지**: 압축 안에 그림이 있으면 모드 정보의 [이미지 보기]로 좌우로 넘겨 볼 수 있습니다.
- **구조가 바뀐 버전 안내**: 새 버전의 옵션 구성이나 설치 위치가 크게 달라졌으면, 이전 버전을 제거한 뒤 설치하도록 안내합니다.
- **게임 실행 버튼**, **4개 언어**(한국어·English·简体中文·日本語), 매니저 새 버전 알림

### 사용법

1. [.NET 8 데스크톱 런타임](https://dotnet.microsoft.com/download/dotnet/8.0)(x64)이 필요합니다.
2. [릴리스](https://github.com/GameStdio77/PBZModManager/releases)에서 `PBZModManager.exe`를 받아, 모드 압축 파일을 모아 둘 폴더에 두고 실행합니다. **이 폴더가 모드 보관 폴더가 되고**, 설치 기록은 그 안의 `.manager` 폴더에 저장됩니다. (예: 게임 폴더 안에 `[] MOD` 폴더를 만들어 두기)
3. 게임 폴더는 자동으로 찾습니다(실행 파일 위쪽 폴더, 각 드라이브의 Steam 라이브러리). 못 찾으면 직접 고르라고 묻고, 나중에 [폴더 설정]에서 바꿀 수 있습니다.
4. Nexus Mods 등에서 받은 모드 압축 파일(zip·rar·7z)을 창에 끌어다 놓습니다. rar·7z는 같은 이름의 zip으로 바꿔 보관합니다.
5. 처음 보는 모드면 정보 입력 창이 뜹니다. **맨 위의 주소 칸에 모드 페이지 주소를 직접 넣어 주세요.** 매니저가 모드 페이지를 알아낼 방법은 이 주소뿐입니다.
   - Nexus 주소(`https://www.nexusmods.com/phantombladezero/mods/번호`)를 넣으면 **카테고리와 설치 방법을 Nexus API로 자동으로 가져옵니다.**
   - GitHub 저장소 주소도 됩니다. 이 경우 GitHub 릴리스로 버전을 확인합니다.
   - 같은 모드의 새 버전을 추가하면 이전 버전의 정보를 그대로 이어받습니다.
6. 모드를 골라 [설치...]를 누르면 미리보기가 나옵니다. 빼고 싶은 파일의 체크를 풀고 설치합니다.
7. [최신 버전 확인]을 누르면 주소를 넣은 모드마다 새 버전이 있는지 확인합니다. 새 버전이 있으면 다운로드 페이지를 열 수 있고, 받은 파일을 다시 끌어다 놓으면 업데이트할 수 있습니다.
8. 오른쪽 위 [게임 실행]으로 게임을 바로 실행할 수 있습니다.

게임이 실행 중일 때는 설치·제거를 막습니다. 언어는 오른쪽 위에서 바꾸며, 다시 시작하면 적용됩니다.

### `.manager` 폴더

| 항목 | 내용 |
|---|---|
| `history/` | 설치 기록과 덮어쓴 원본 백업. 제거·되돌리기에 필요하니 지우지 마세요. |
| `leftovers/` | 제거할 때 지우지 않고 옮겨 둔 파일. [보관 폴더 비우기]로 휴지통에 보낼 수 있습니다. |
| `meta.json` | 모드별 카테고리·주소·메모·실행 도구·설치 방법 |
| `settings.json` | 게임 폴더, JS 모드 실행기, 언어, 창 위치 |
| `order.json`, `categories.json`, `remote.json`, `log.txt` | 목록 순서·구분선, 카테고리, 최신 버전 확인 결과, 작업 기록 |

### 네트워크 사용

- Nexus Mods 공개 GraphQL API(`api.nexusmods.com/v2/graphql`): 주소를 넣었을 때 카테고리·설치 방법, [최신 버전 확인]을 눌렀을 때 최신 버전. API 키를 쓰지 않습니다.
- GitHub 릴리스 API(`api.github.com`): GitHub 주소인 모드의 최신 버전, 그리고 시작할 때 한 번 이 매니저의 새 버전
- 파일을 내려받지는 않고, 다운로드 페이지를 브라우저로 열어 줍니다.

### 크레딧

- 이 프로그램은 Anthropic의 AI [Claude](https://claude.com/claude-code)(Claude Code)를 이용해 개발했습니다.
- rar·7z 읽기: [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT)
- 라이선스: [GNU General Public License v3.0](LICENSE)

---

## English

A mod manager for Phantom Blade Zero. It installs downloaded mod archives where the game and its mod loaders read them, records every file it touches, and can put the game folder back exactly as it was.

> - **Made for the Steam version.** The game folder layout and the game program (`platform\steam\PBZ.exe`) are handled as in the Steam release.
> - Unofficial community tool, not affiliated with the game's developer or Nexus Mods.

### Features

- **Safe install and removal**: every file an install overwrites is backed up, and removing a mod restores it. Files changed or created after install are moved to a kept-files folder rather than deleted.
- **Preview and file picking**: before installing, see where every file goes, grouped by folder or by step, and untick files you don't want. Left-out files stay left out on later updates.
- **Right place automatically**
  - JS mods: placed for PBZModLoader (`ProjectPBZ/PBZModLoader/mods`) or Mods Menu (`.../JavaScript/PBZModsMenu/Mods`), whichever you use
  - UE4SS itself in `platform/steam`, UE4SS Lua mods in `platform/steam/ue4ss/Mods`
  - pak mods in `Content/Paks/~mods`, Blueprint mods in `Content/Paks/LogicMods`
  - "(choose one)" option folders that share files: one per group
- **Nexus info filled in**: enter a mod's address and its category and install steps are fetched with the public Nexus API.
- **Update check**: checks Nexus Mods and GitHub releases for newer versions and opens the download page.
- **Versions**: several versions of a mod are one mod to update or downgrade; old version files can be cleaned up (to the Recycle Bin).
- **Organizing**: reorder installed mods, named separators, one-line memos, install or remove several mods at once
- **Preview pictures**: when an archive holds pictures, [Pictures] in the mod info pages through them.
- **Changed layouts**: when a new version's options or install locations changed a lot, you are guided to remove the old version first.
- **Play button**, **four languages** (한국어 · English · 简体中文 · 日本語), notice when a new manager version is out

### Usage

1. Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
2. Download `PBZModManager.exe` from [Releases](https://github.com/GameStdio77/PBZModManager/releases), put it in the folder where you keep mod archives, and run it. **That folder becomes the mod folder**, and the records go in its `.manager` folder (for example, a `[] MOD` folder inside the game folder).
3. The game folder is found automatically (folders above the exe, Steam libraries on every drive). If not, you are asked once; change it later in [Folder settings].
4. Drop mod archives (zip, rar or 7z, from Nexus Mods or elsewhere) on the window. A rar or 7z is kept as a zip of the same name.
5. For a mod it has not seen, the info window opens. **Type the mod page's address in the address field at the top.** The address is the only way the manager knows the mod's page.
   - With a Nexus address (`https://www.nexusmods.com/phantombladezero/mods/<number>`), **the category and install steps are fetched automatically through the Nexus API.**
   - A GitHub repository address works too; versions are then checked against its GitHub releases.
   - A new version of a known mod takes over the earlier version's info.
6. Pick a mod and press [Install...] to see the preview. Untick files you want left out, then install.
7. [Check for updates] checks every mod that has an address. When a newer version is out, open its download page and drop the downloaded file on the window to update.
8. [Play] at the top right starts the game.

Installing and removing are blocked while the game runs. The language is picked at the top right and applies after a restart.

### The `.manager` folder

| Item | Contents |
|---|---|
| `history/` | Install records and backups of overwritten files. Needed to remove and undo; do not delete. |
| `leftovers/` | Files moved aside instead of deleted on removal. [Empty kept files] sends them to the Recycle Bin. |
| `meta.json` | Each mod's category, address, memo, tool, install steps |
| `settings.json` | Game folder, JS mod runner, language, window place |
| `order.json`, `categories.json`, `remote.json`, `log.txt` | List order and separators, categories, update check results, work log |

### Network use

- Nexus Mods public GraphQL API (`api.nexusmods.com/v2/graphql`): category and install steps when an address is entered, latest versions when [Check for updates] is pressed. No API key is used.
- GitHub releases API (`api.github.com`): latest versions of mods kept on GitHub, and once at start, whether this manager has a new version.
- Nothing is downloaded; download pages open in your browser.

### Credits

- Developed with Anthropic's AI [Claude](https://claude.com/claude-code) (Claude Code).
- rar and 7z reading: [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT)
- License: [GNU General Public License v3.0](LICENSE)

---

## 简体中文

Phantom Blade Zero（影之刃零）的模组管理器。它会把下载的模组压缩包安装到游戏文件夹中正确的位置，记录所有改动过的文件，并能把游戏文件夹完整恢复到原来的状态。

> - **以 Steam 版为准**制作。游戏文件夹结构和游戏程序（`platform\steam\PBZ.exe`）按 Steam 版处理。
> - 非官方社区工具，与游戏开发商及 Nexus Mods 无关。

### 主要功能

- **安全的安装与移除**：安装时备份所有被覆盖的文件，移除时恢复原状。安装后被修改或新生成的文件不会删除，而是移到保留文件夹。
- **预览与选择文件**：安装前按路径或按操作显示每个文件的去向，取消勾选即可排除不想要的文件。排除的文件在以后的更新中也保持排除。
- **自动放到正确位置**
  - JS 模组：按你使用的 PBZModLoader（`ProjectPBZ/PBZModLoader/mods`）或 Mods Menu（`.../JavaScript/PBZModsMenu/Mods`）放置
  - UE4SS 本体放在 `platform/steam`，UE4SS Lua 模组放在 `platform/steam/ue4ss/Mods`
  - pak 模组放在 `Content/Paks/~mods`，蓝图模组放在 `Content/Paks/LogicMods`
  - 像“(choose one)”这样共用文件的选项文件夹，每组选一个安装
- **自动填写 Nexus 信息**：填写模组地址后，会通过 Nexus 公开 API 获取分类和说明中的安装方法。
- **检查最新版本**：在 Nexus Mods 和 GitHub Releases 上检查是否有新版本，并打开下载页面。
- **版本管理**：同一模组的多个版本作为一个模组来更新或降级，旧版本文件可以清理（移到回收站）。
- **整理工具**：调整已安装模组的顺序、带名称的分隔线、一行备注、一次安装或移除多个模组
- **预览图片**：压缩包中有图片时，可在模组信息中点击 [查看图片] 左右翻看。
- **结构变更提示**：新版本的选项构成或安装位置变化较大时，会提示先移除旧版本再安装。
- **启动游戏按钮**、**四种语言**（한국어 · English · 简体中文 · 日本語）、管理器新版本提醒

### 使用方法

1. 需要 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（x64）。
2. 从 [Releases](https://github.com/GameStdio77/PBZModManager/releases) 下载 `PBZModManager.exe`，放到存放模组压缩包的文件夹中运行。**该文件夹即为模组存放文件夹**，安装记录保存在其中的 `.manager` 文件夹里（例如在游戏文件夹中建一个 `[] MOD` 文件夹）。
3. 会自动查找游戏文件夹（程序所在位置的上级文件夹、各磁盘的 Steam 库）。找不到时会让你选择一次，之后可在 [文件夹设置] 中更改。
4. 把从 Nexus Mods 等处下载的模组压缩包（zip、rar、7z）拖到窗口中。rar 和 7z 会转换为同名的 zip 保存。
5. 遇到新模组时会弹出信息窗口。**请在最上方的地址栏中手动填写模组页面地址。** 地址是管理器得知模组页面的唯一方式。
   - 填写 Nexus 地址（`https://www.nexusmods.com/phantombladezero/mods/编号`）后，**会通过 Nexus API 自动获取分类和安装方法。**
   - 也可以填写 GitHub 仓库地址，此时通过 GitHub Releases 检查版本。
   - 添加已知模组的新版本时，会沿用旧版本的信息。
6. 选择模组后点击 [安装...] 会显示预览。取消勾选不想安装的文件，然后安装。
7. 点击 [检查最新版本] 会检查每个填写了地址的模组。有新版本时可以打开下载页面，把下载的文件拖到窗口即可更新。
8. 点击右上角的 [启动游戏] 可直接启动游戏。

游戏运行时无法安装或移除。语言在右上角选择，重新启动后生效。

### `.manager` 文件夹

| 项目 | 内容 |
|---|---|
| `history/` | 安装记录和被覆盖原文件的备份。移除和撤销时需要，请勿删除。 |
| `leftovers/` | 移除时没有删除而移到这里的文件。可用 [清空保留文件夹] 移到回收站。 |
| `meta.json` | 每个模组的分类、地址、备注、运行工具、安装方法 |
| `settings.json` | 游戏文件夹、JS 模组运行器、语言、窗口位置 |
| `order.json`、`categories.json`、`remote.json`、`log.txt` | 列表顺序与分隔线、分类、最新版本检查结果、操作日志 |

### 网络使用

- Nexus Mods 公开 GraphQL API（`api.nexusmods.com/v2/graphql`）：填写地址时获取分类和安装方法，点击 [检查最新版本] 时获取最新版本。不使用 API 密钥。
- GitHub Releases API（`api.github.com`）：托管在 GitHub 上的模组的最新版本，以及启动时检查一次本管理器是否有新版本。
- 不会下载文件，只会在浏览器中打开下载页面。

### 致谢

- 本程序使用 Anthropic 的 AI [Claude](https://claude.com/claude-code)（Claude Code）开发。
- rar、7z 读取：[SharpCompress](https://github.com/adamhathcock/sharpcompress)（MIT）
- 许可证：[GNU General Public License v3.0](LICENSE)

---

## 日本語

Phantom Blade Zero 用の Mod マネージャーです。ダウンロードした Mod の圧縮ファイルをゲームフォルダーの正しい場所にインストールし、変更したファイルをすべて記録して、ゲームフォルダーを元どおりに戻せるようにします。

> - **Steam 版を基準**に作っています。ゲームフォルダーの構成と実行ファイル（`platform\steam\PBZ.exe`）は Steam 版に合わせて扱います。
> - 非公式のコミュニティツールです。ゲームの開発元や Nexus Mods とは関係ありません。

### 主な機能

- **安全なインストールと削除**：インストール時に上書きするファイルをすべてバックアップし、削除すると元に戻します。インストール後に変更されたファイルや Mod が作ったファイルは消さずに保管フォルダーへ移します。
- **プレビューとファイル選択**：インストール前に各ファイルの行き先をパス別・作業別に表示し、不要なファイルはチェックを外して除外できます。除外したファイルは次のアップデートでも除外されます。
- **正しい場所に自動でインストール**
  - JS Mod：使っている PBZModLoader（`ProjectPBZ/PBZModLoader/mods`）または Mods Menu（`.../JavaScript/PBZModsMenu/Mods`）に合わせて配置
  - UE4SS 本体は `platform/steam`、UE4SS の Lua Mod は `platform/steam/ue4ss/Mods`
  - pak Mod は `Content/Paks/~mods`、ブループリント Mod は `Content/Paks/LogicMods`
  - 「(choose one)」のように同じファイルを使うオプションフォルダーは、グループごとに 1 つ選んでインストール
- **Nexus の情報を自動入力**：Mod のアドレスを入れると、Nexus の公開 API でカテゴリーと説明文のインストール方法を取得します。
- **最新バージョン確認**：Nexus Mods と GitHub のリリースで新しいバージョンがあるか確認し、ダウンロードページを開きます。
- **バージョン管理**：同じ Mod の複数バージョンを 1 つの Mod として更新・ダウングレードし、旧バージョンのファイルを整理（ごみ箱へ）できます。
- **整理ツール**：インストール済み Mod の並べ替え、名前付きの区切り線、1 行メモ、複数の Mod の一括インストール・削除
- **プレビュー画像**：圧縮ファイルに画像があれば、Mod 情報の [画像を見る] で左右にめくって見られます。
- **構成が変わったバージョンの案内**：新しいバージョンのオプション構成やインストール先が大きく変わった場合、以前のバージョンを削除してからインストールするよう案内します。
- **ゲーム起動ボタン**、**4 言語**（한국어 · English · 简体中文 · 日本語）、マネージャーの新バージョン通知

### 使い方

1. [.NET 8 デスクトップランタイム](https://dotnet.microsoft.com/download/dotnet/8.0)（x64）が必要です。
2. [Releases](https://github.com/GameStdio77/PBZModManager/releases) から `PBZModManager.exe` をダウンロードし、Mod の圧縮ファイルを置くフォルダーに入れて実行します。**そのフォルダーが Mod 保管フォルダーになり**、インストール記録はその中の `.manager` フォルダーに保存されます（例：ゲームフォルダー内に `[] MOD` フォルダーを作る）。
3. ゲームフォルダーは自動で探します（実行ファイルの上位フォルダー、各ドライブの Steam ライブラリ）。見つからない場合は一度だけ選ぶよう求められ、後から [フォルダー設定] で変更できます。
4. Nexus Mods などからダウンロードした Mod の圧縮ファイル（zip・rar・7z）をウィンドウにドラッグします。rar・7z は同じ名前の zip に変換して保管します。
5. 初めての Mod なら情報入力ウィンドウが開きます。**一番上のアドレス欄に Mod ページのアドレスを手で入力してください。** マネージャーが Mod のページを知る方法はこのアドレスだけです。
   - Nexus のアドレス（`https://www.nexusmods.com/phantombladezero/mods/番号`）を入れると、**カテゴリーとインストール方法を Nexus API で自動取得します。**
   - GitHub リポジトリのアドレスも使えます。その場合は GitHub のリリースでバージョンを確認します。
   - 既知の Mod の新しいバージョンを追加すると、以前のバージョンの情報を引き継ぎます。
6. Mod を選んで [インストール...] を押すとプレビューが出ます。除外したいファイルのチェックを外してインストールします。
7. [最新バージョン確認] を押すと、アドレスを入れた Mod ごとに新しいバージョンがあるか確認します。新しいバージョンがあればダウンロードページを開き、ダウンロードしたファイルをウィンドウにドラッグすると更新できます。
8. 右上の [ゲーム起動] でゲームをすぐに起動できます。

ゲームの実行中はインストール・削除できません。言語は右上で選び、再起動後に反映されます。

### `.manager` フォルダー

| 項目 | 内容 |
|---|---|
| `history/` | インストール記録と上書きした元ファイルのバックアップ。削除・元に戻すときに必要なので消さないでください。 |
| `leftovers/` | 削除時に消さずに移したファイル。[保管フォルダーを空にする] でごみ箱に送れます。 |
| `meta.json` | Mod ごとのカテゴリー・アドレス・メモ・実行ツール・インストール方法 |
| `settings.json` | ゲームフォルダー、JS Mod 実行環境、言語、ウィンドウ位置 |
| `order.json`、`categories.json`、`remote.json`、`log.txt` | 一覧の順番と区切り線、カテゴリー、最新バージョン確認の結果、作業記録 |

### ネットワークの使用

- Nexus Mods 公開 GraphQL API（`api.nexusmods.com/v2/graphql`）：アドレスを入れたときにカテゴリー・インストール方法、[最新バージョン確認] を押したときに最新バージョン。API キーは使いません。
- GitHub リリース API（`api.github.com`）：GitHub で公開されている Mod の最新バージョン、および起動時に一度このマネージャーの新バージョン。
- ファイルはダウンロードせず、ダウンロードページをブラウザーで開きます。

### クレジット

- このプログラムは Anthropic の AI [Claude](https://claude.com/claude-code)（Claude Code）を使って開発しました。
- rar・7z の読み込み：[SharpCompress](https://github.com/adamhathcock/sharpcompress)（MIT）
- ライセンス：[GNU General Public License v3.0](LICENSE)
