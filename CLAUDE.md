# DatasheetGenerator 에이전트

## 프로젝트 개요

게임 데이터 시트를 관리하는 WPF 데스크톱 앱.
JSON 스키마를 정의하고, WebView2 + Handsontable UI로 데이터를 입력·검증·저장한다.
저장 결과는 내부 JSON(AppData), 도메인별 JSON(OutputRoot), Excel(.xlsx) 세 가지로 출력된다.

**스택**: C# / .NET 10 / WPF / WebView2 / Handsontable / Newtonsoft.Json / ClosedXML / xUnit

---

## 디렉토리 구조

```
DatasheetGenerator/
  Configuration/   AppConfig, AppConfigLoader, AppConfigLoadResult
  Export/          ExportPathProvider, SpreadsheetWriter, ValidationResult, ExportPaths
  Models/          SchemaColumn, FlatColumn, SchemaInfo, PivotInfo, SchemaGraph*
  Services/        SchemaService, DataEntryService, SchemaGraphService, SchemaGraphWriter
  *.xaml.cs        UI 진입점 (MainWindow, DataEntryWindow, SchemaEditorWindow, SchemaGraphWindow)
DatasheetGenerator.Tests/
  *Tests.cs        xUnit 단위·통합 테스트 (서비스 레이어 대상)
docs/
  Doctrine.md          코딩 철학 (단순함, 외과적 변경, 목표 중심)
  CodeStyle.md         C# 코드 스타일 규칙
  ProjectCommonRule.md 작업 절차 규칙 (빌드·포맷·테스트·완료 보고)
  Convention.md        위 두 문서의 인덱스
```

---

## 빌드 · 테스트 · 포맷 명령

```powershell
# 빌드
dotnet build C:\depot\Work\DatasheetGenerator\DatasheetGenerator.slnx

# 테스트
dotnet test C:\depot\Work\DatasheetGenerator\DatasheetGenerator.slnx

# 포맷 검사 (변경 없어야 정상)
dotnet format C:\depot\Work\DatasheetGenerator\DatasheetGenerator.slnx --verify-no-changes
```

코드 변경 후 반드시 **빌드 → 테스트 → 포맷 검사** 순으로 실행한다.

---

## 핵심 흐름 요약

### 데이터 저장 경로
- **내부 JSON**: `%LocalAppData%\DatasheetGenerator\data\{schema}.json` (DataEntryWindow 기본 저장)
- **도메인 JSON**: `{OutputRoot}\{domain}\{schema}.json` (도메인별 분리 저장)
- **Excel**: `{OutputRoot}\Excel\{schema}.xlsx`

### 스키마 파일
- 위치: `{OutputRoot}\schema\{schema}.schema.json`
- 지원 타입: `string`, `integer`, `number`, `boolean`, `object`, `array`
- 특수 필드: `ref` (다른 스키마 컬럼 참조), `domain` (도메인 필터), `pivot` (피벗 구조)

### FlatColumn
`SchemaColumn` 계층(object/array 중첩)을 1차원 테이블 컬럼으로 전개한 것.
`Path`가 Handsontable 컬럼 키이자 DataTable 컬럼명이다.

---

## 작업 범위 제한 (필수)

모든 작업은 `C:\depot\Work\DatasheetGenerator\` 디렉토리 내부로만 한정한다.

- 이 프로젝트 외부의 파일(다른 프로젝트 코드, 공유 컨벤션 문서 등)은 읽기만 허용하며 수정하지 않는다.
- `docs/` 또는 `Convention/` 문서를 수정할 때는 ProjectCommonRule.md 4번 규칙에 따라 반드시 사용자 승인을 받는다.

---

## 작업 규칙 (필수)

이 섹션은 모든 코드 작업 시 항상 적용한다.

@docs/Doctrine.md
@docs/CodeStyle.md
@docs/ProjectCommonRule.md

---

## 알려진 미해결 항목 (코드 외)

- IME 입력 동작 (#11): 수동 QA 필요
- RevoGrid 라이선스 (#14): MIT, 상용 사용 가능. `@revolist/revogrid@4.23.7` 기준.
- 대용량 데이터 성능 (#17): 임계값 불명확, 문제 발생 시 대응
- 고DPI 렌더링 (#18): 문제 발생 시 대응
- 배포 시 DLL 누락 (#20): 배포 패키징 시 점검
