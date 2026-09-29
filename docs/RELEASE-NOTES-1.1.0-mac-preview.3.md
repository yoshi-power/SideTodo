# SideTodo for Mac · Preview 3

Preview 2 업데이트 후 바에 마우스를 올려도 할 일 목록이 나타나지 않는 오류를 수정했습니다.

Mac 창의 기본 빈 화면을 이미 만들어진 할 일 화면으로 잘못 판단해, 실제 목록을 생성하지 않던 문제였습니다. 이제 할 일 화면의 생성 여부를 별도로 관리합니다. 처음 실행했을 때부터 목록이 만들어지고, 닫았다 다시 열어도 유지됩니다.

이전 검증은 창 표시 여부 위주라 빈 창을 놓쳤습니다. 이번에는 새 실행 → 표시 바 → 호버 → 실제 입력 컨트롤 표시 → 클릭·입력 → Enter 저장 → 닫기·다시 열기를 검증합니다. Apple Silicon과 Intel 모두 같은 UI 검증을 실행합니다.

## 업데이트

1. 기존 앱을 메뉴 막대에서 **SideTodo 종료**로 종료합니다.
2. `SideTodo-1.1.0-mac-preview.3-universal.zip`을 풀고 응용 프로그램 폴더의 **SideTodo.app만 교체**합니다.
3. 새 앱을 실행해 바에 마우스를 올립니다. 메뉴 막대의 **할 일 열기**로도 열 수 있습니다.

일정 데이터 저장 위치·JSON 형식·앱 식별자는 변경하지 않았습니다. `~/Library/Application Support/SideTodo/`는 삭제하지 마세요. 기존 일정·완료 기록·메모와 Preview 2의 원본 백업은 유지됩니다.

macOS 13 이상 대상 / Apple Silicon·Intel Universal. Developer ID 서명·Apple 공증이 없는 테스트 버전이며 첫 실행 안내는 ZIP의 `START-HERE.ko.md`를 확인하세요.

Windows 1.0.1은 이번 수정 대상이 아닙니다. 기존 Windows 수정 ZIP은 Preview 2 릴리스에서 받을 수 있습니다.
