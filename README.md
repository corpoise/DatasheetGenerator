# DatasheetGenerator

게임 데이터를 JSON Schema 기반으로 정의하고, 스키마에 맞춰 데이터를 입력·검증·저장하는 WPF 데스크톱 도구입니다.

---

## Features

- **JSON Schema 생성 및 편집** — 계층 구조(object, array 중첩)를 지원하는 스키마 정의
- **스키마 기반 데이터 입력** — Handsontable 스프레드시트 UI로 데이터 입력 및 인라인 검증
- **스키마 구조 보기** — 스키마 간 참조 관계를 그래프로 시각화
- **다중 포맷 Export** — 내부 JSON, 도메인별 JSON, Excel(.xlsx) 동시 출력

---

## Schema Graph View

스키마 간 참조 관계를 그래프로 시각화합니다. 선택한 스키마를 중심으로 순방향·역방향·자기 참조를 한눈에 확인할 수 있습니다.

**CommonSkillSet** — ActiveSkill, PassiveSkill, BattleEffect 스키마와의 참조 관계

![CommonSkillSet 구조 보기](docs/images/schema-graph-CommonSkillSet.png)

**StageTowerLevel** — Stage, Monster 스키마와의 참조 관계 (역방향 참조 포함)

![StageTowerLevel 구조 보기](docs/images/schema-graph-StageTowerLevel.png)

---

## Export Formats

| Format | 경로 | 설명 |
|--------|------|------|
| 내부 JSON | `%LocalAppData%\DatasheetGenerator\data\` | 앱 내부 저장용 |
| 도메인 JSON | `{OutputRootPath}\{domain}\` | 도메인별 필드 필터링 결과 |
| Excel | `{OutputRootPath}\Excel\` | `.xlsx` 형식 |

---

## Getting Started

### 1. appsettings.json 설정

`DatasheetGenerator/appsettings.example.json`을 복사하여 같은 경로에 `DatasheetGenerator/appsettings.json`으로 저장한 후 `OutputRootPath`를 실제 출력 경로로 수정합니다.

```json
{
  "OutputRootPath": "C:\\your\\output\\path",
  "Domains": ["json"]
}
```

### 2. Handsontable 파일 준비

라이선스 문제로 Handsontable 파일은 레포지토리에 포함되어 있지 않습니다.
[Handsontable 공식 사이트에서](https://handsontable.com) 다운로드한 후 아래 경로에 배치합니다.

```
DatasheetGenerator/wwwroot/handsontable.full.min.js
DatasheetGenerator/wwwroot/handsontable.full.min.css
```

### 3. 빌드

```powershell
dotnet build DatasheetGenerator.slnx
```

---

## Tech Stack

- C# / .NET 10 / WPF
- WebView2 + Handsontable
- Newtonsoft.Json
- xUnit

---

*This project was generated with [Claude Code](https://claude.ai/code).*
