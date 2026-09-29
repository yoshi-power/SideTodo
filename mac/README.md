# SideTodo for macOS · Preview 2

macOS 13 이상 / Apple Silicon + Intel Universal 앱입니다. Swift·SwiftUI·AppKit만 사용하며 외부 라이브러리가 없습니다.

Windows 버전의 기능과 데이터 구조를 유지하면서 macOS의 보조 패널과 메뉴 막대에 맞춰 새로 구현했습니다.

- Dock / ⌘Tab에 표시하지 않는 메뉴 막대 앱 (`LSUIElement`).
- 호버 시 다른 앱의 키보드 포커스를 빼앗지 않는 `NSPanel`.
- 화면 가장자리의 작은 표시, 넉넉한 감지 영역과 닫힘 지연.
- 오늘 / 앞으로, 한글 IME를 고려한 바로 입력, Shift-Return 줄바꿈.
- 긴 제목과 메모는 줄바꿈하며 높이가 늘어납니다. 화면 높이를 넘어가면 스크롤합니다.
- 호버 상세 편집, 드래그 가능한 상세창, 커스텀 달력.
- 완료 기록 정렬 / 복구 / JSON·Markdown 내보내기.
- 모니터 선택, 전체 화면 표시 선택, 호버 일시 중지.
- macOS 동작 줄이기 설정 준수. 위치 확인에 접근성·화면 기록 권한을 요구하지 않습니다.
- 로컬 저장, 원자적 파일 교체, 이전 파일 백업, 손상 데이터 보호.

## 빌드

Mac의 Xcode Command Line Tools와 Swift 5.9 이상이 필요합니다.

```sh
bash mac/build.sh
# Mac GUI 세션에서 창 검증까지
bash mac/build.sh --ui-smoke
```

결과: `dist/mac/SideTodo.app`, Universal ZIP, SHA256 및 안내문.
GitHub Actions의 `macOS Preview` 워크플로에서도 빌드합니다.

코드에는 macOS 13 최소 버전을 지정했습니다. CI 빌드 / 자동 검증 OS와 실제 최소 OS의 수동 검증은 별개입니다.
자동 창 검증은 생성·표시·크기·복구를 검사합니다. IME, Spaces, Stage Manager, 전체 화면 앱, 모니터 연결/해제, 호버 사용감은 실제 Mac에서 확인해야 합니다.

## 데이터

`~/Library/Application Support/SideTodo/tasks.json` 및 `tasks.json.bak`.
Preview 2 첫 실행은 기존 파일을 `tasks.before-preview2.json`으로 별도 보존합니다. 앱 교체 시 저장 위치·형식과 앱 식별자는 유지됩니다.
Windows와 같은 `Tasks`, `Id`, `Title`, `Notes`, `Due`, `Done`, `CompletedAt`, `Created` JSON 필드를 사용합니다.
메뉴 막대의 가져오기는 기존 ID를 건너뛰고 새 일정만 합칩니다. 동기화 기능은 아닙니다.
완료 JSON 내보내기는 같은 포맷으로 완료 항목만 담습니다.

## 배포 상태

친구 테스트용 프리뷰입니다. ARM 실행을 위한 ad-hoc 서명이 있으며 **Developer ID 서명 / Apple 공증은 없습니다**. 첫 실행 안내는 [MAC-TESTING.ko.md](../docs/MAC-TESTING.ko.md)를 확인하세요.
로그인 시 자동 시작, 알림, 클라우드 동기화는 포함하지 않습니다.

Apple API 참고: [NSPanel](https://developer.apple.com/documentation/appkit/nspanel), [nonactivatingPanel](https://developer.apple.com/documentation/appkit/nswindow/stylemask-swift.struct/nonactivatingpanel), [fullScreenAuxiliary](https://developer.apple.com/documentation/appkit/nswindow/collectionbehavior-swift.struct/fullscreenauxiliary).
