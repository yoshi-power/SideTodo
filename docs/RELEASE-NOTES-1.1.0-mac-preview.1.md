# SideTodo for Mac · Preview 1

Windows에서 사용하던 작은 할 일 위젯을 macOS용 네이티브 앱으로 옮긴 첫 테스트 버전입니다.

## 받기

`SideTodo-1.1.0-mac-preview.1-universal.zip` 하나를 받으면 됩니다. Apple Silicon(M1 이후)과 Intel Mac을 함께 지원합니다. macOS 13 이상이 필요합니다.

ZIP 안의 `SideTodo.app`을 응용 프로그램 폴더에 옮기고 실행하세요. `START-HERE.ko.md`에 첫 실행과 테스트 안내가 들어 있습니다.

**Developer ID 서명 / Apple 공증이 없는 프리뷰입니다.** 첫 실행 시 차단될 수 있습니다. 배포자를 신뢰하는 경우 실행 시도 후 시스템 설정 → 개인정보 보호 및 보안 → 확인 없이 열기를 이용하세요. [Apple 안내](https://support.apple.com/en-us/102445)를 참고하세요.

## Mac에 맞춘 동작

- 메뉴 막대에 상주하며 Dock·⌘Tab에는 나타나지 않습니다.
- 가장자리 호버로 열어도 작업 중인 앱의 키보드 포커스를 가져오지 않습니다.
- 오늘 / 앞으로, 바로 입력, 한글 조합 보호, Shift-Return 줄바꿈, ⌘C/V/Z, ⌘W, Escape.
- 호버 상세 편집, 상단 드래그, 내용에 따라 늘어나는 메모, 작은 달력.
- 완료 기록 정렬·복구·JSON/Markdown 내보내기, Windows JSON 가져오기.
- 모니터 선택, 전체 화면 표시 선택, 호버 일시 중지, 동작 줄이기 설정 반영.
- 데이터 로컬 저장과 백업. 자동 시작·알림·클라우드 동기화는 포함하지 않습니다.

## 검증과 남은 확인

GitHub의 Apple Silicon Mac에서 Universal 빌드와 데이터 검증, AppKit 창 자동 검증을 실행하고, Intel Mac에서도 같은 배포 앱의 데이터 검증을 실행합니다. 렌더링한 위젯과 긴 메모 화면도 확인합니다.

실제 사용자 환경의 한글 IME, 전체 화면·Spaces·Stage Manager, 모니터 연결/해제, 트랙패드 사용감과 macOS 13 최소 버전은 친구 테스트에서 확인할 대상입니다. [테스트 체크리스트](https://github.com/yoshi-power/SideTodo/blob/main/docs/MAC-TESTING.ko.md)를 참고해 주세요.

기존 Windows 1.0.0 배포 파일과 일정 데이터는 변경하지 않습니다.
