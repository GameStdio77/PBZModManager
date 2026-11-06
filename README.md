# PBZ Mod Manager

Phantom Blade Zero(팬텀 블레이드 제로)용 모드 매니저입니다. 받은 모드 압축 파일을 게임 폴더의 알맞은 위치에 설치하고, 무엇을 설치했는지 기록해 두었다가 그대로 되돌릴 수 있게 해 줍니다.

A mod manager for Phantom Blade Zero (Windows). It installs mod archives where the game and its mod loaders read them, records every file it touches, and can put the game folder back exactly as it was. English notes are at the end.

> 비공식 커뮤니티 도구입니다. 게임 제작사나 Nexus Mods와 관련이 없습니다.

## 주요 기능

- **안전한 설치와 제거**: 설치할 때 덮어쓰는 파일을 모두 백업하고, 제거하면 원래 상태로 되돌립니다. 설치 후 바뀐 파일이나 모드가 만든 파일은 지우지 않고 보관 폴더로 옮깁니다.
- **미리보기**: 설치 전에 어떤 파일이 어디로 가는지 경로별·작업별로 보여 주고, 원하지 않는 파일은 체크를 풀어 뺄 수 있습니다. 뺀 파일은 다음 업데이트에도 그대로 빠집니다.
- **알아서 맞는 자리에 설치**
  - JS 모드: PBZModLoader(`ProjectPBZ/PBZModLoader/mods`)와 Mods Menu(`.../JavaScript/PBZModsMenu/Mods`) 중 쓰는 쪽에 맞춰 배치
  - UE4SS 본체는 `platform/steam`, UE4SS Lua 모드는 `platform/steam/ue4ss/Mods`
  - pak 모드는 `Content/Paks/~mods`, 블루프린트 모드는 `Content/Paks/LogicMods`
  - "(choose one)"처럼 같은 파일을 쓰는 옵션 폴더는 묶음마다 하나씩 골라 설치
- **버전 관리**: 같은 모드의 여러 버전을 한 모드로 묶어 업데이트·다운그레이드합니다. Nexus 페이지 번호와 모드 이름이 같으면 실행 도구가 바뀐 버전도 한 모드로 봅니다.
- **최신 버전 확인**: Nexus Mods(공개 API, 키 불필요)와 GitHub 릴리스를 버튼을 누를 때만 확인합니다. 파일 이름에 버전이 없어도 GitHub 릴리스의 SHA-256으로 버전을 알아냅니다.
- **모드 정보**: 카테고리(Nexus 기준), 주소, 한 줄 메모, 실행 도구, Nexus 설명에서 가져온 설치 방법
- **정리 도구**: 설치된 모드 순서 바꾸기와 이름 붙인 구분선, 여러 모드 한 번에 설치·제거, 이전 버전 파일 정리(휴지통으로)

## 사용법

1. [.NET 8 데스크톱 런타임](https://dotnet.microsoft.com/download/dotnet/8.0)(x64)이 필요합니다.
2. `PBZModManager.exe`를 모드 압축 파일을 모아 둘 폴더에 두고 실행합니다. **이 폴더가 모드 보관 폴더가 되고**, 설치 기록은 그 안의 `.manager` 폴더에 저장됩니다. (예: 게임 폴더 안에 `[] MOD` 폴더를 만들어 두기)
3. 게임 폴더는 자동으로 찾습니다(실행 파일 위쪽 폴더, 각 드라이브의 Steam 라이브러리). 못 찾으면 직접 고르라고 묻고, 나중에 [폴더 설정]에서 바꿀 수 있습니다.
4. 받은 모드 zip을 창에 끌어다 놓고, 모드를 골라 [설치...]를 누르면 미리보기가 나옵니다.

게임이 실행 중일 때는 설치·제거를 막습니다.

### `.manager` 폴더

| 항목 | 내용 |
|---|---|
| `history/` | 설치 기록과 덮어쓴 원본 백업. 제거·되돌리기에 필요하니 지우지 마세요. |
| `leftovers/` | 제거할 때 지우지 않고 옮겨 둔 파일. [보관 폴더 비우기]로 휴지통에 보낼 수 있습니다. |
| `meta.json` | 모드별 카테고리·주소·메모·설치 방법 |
| `settings.json` | 게임 폴더, JS 모드 실행기 선택, 창 위치 |
| `order.json`, `categories.json`, `remote.json`, `log.txt` | 목록 순서·구분선, 카테고리, 최신 버전 확인 결과, 작업 기록 |

## 빌드

Windows와 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

```bash
dotnet publish PBZModManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
```

- `Tests/`: 설치·제거·배치 규칙을 임시 폴더에서 확인하는 테스트 (`dotnet run --project Tests -- <빈 작업 폴더>`)
- `UiChecks/`: 화면에 보이지 않는 별도 데스크톱에서 창을 띄워 확인하는 점검 (`dotnet run --project UiChecks -- <빈 작업 폴더>`)

둘 다 실제 게임 폴더는 건드리지 않습니다.

## 네트워크 사용

매니저가 인터넷에 접속하는 것은 사용자가 버튼을 누르거나 주소를 입력했을 때뿐입니다.

- Nexus Mods 공개 GraphQL API(`api.nexusmods.com/v2/graphql`): 최신 버전, 카테고리, 설치 방법. API 키를 쓰지 않습니다.
- GitHub 릴리스 API(`api.github.com`): 주소가 GitHub 저장소인 모드의 최신 버전
- 다운로드는 하지 않고, 다운로드 페이지를 브라우저로 열어 줍니다.

## 라이선스

[GNU General Public License v3.0](LICENSE)

---

## English

- Put `PBZModManager.exe` in the folder where you keep mod archives; that folder holds the records in `.manager`. The game folder is found automatically or picked once.
- Drop mod zips on the window, pick one, and press Install: a preview lists every file by folder or by step, and unticked files are left out.
- Every overwritten file is backed up, so removing a mod restores the game folder exactly.
- Handles PBZModLoader and Mods Menu script mods, UE4SS and its Lua mods, pak and Blueprint mods, and "(choose one)" option folders.
- Latest-version checks use the public Nexus Mods API (no key) and GitHub releases, only when you press the button.
- Build: `dotnet publish PBZModManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish` (.NET 8, Windows).
- Unofficial community tool, not affiliated with the game's developer or Nexus Mods. Licensed under GPL-3.0.
