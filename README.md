DatasheetGenerator

DatasheetGenerator는 JSON Schema를 생성하고 편집하며, 해당 스키마를 기반으로 데이터를 입력할 수 있는 도구입니다.

입력된 JSON Schema와 데이터를 바탕으로 JSON 파일 및 Excel 파일로 Export 하는 기능을 제공합니다.

Features
JSON Schema 생성
JSON Schema 편집
스키마 기반 데이터 입력
입력된 스키마와 데이터를 통한 JSON Export
입력된 스키마와 데이터를 통한 Excel Export
Purpose

반복적으로 관리해야 하는 데이터 구조를 JSON Schema로 정의하고, 정의된 스키마에 맞춰 데이터를 입력한 뒤 JSON 또는 Excel 형식으로 출력하는 것을 목표로 합니다.

이를 통해 데이터 정의, 입력, 검증, 출력 과정을 하나의 흐름으로 관리할 수 있습니다.

Export Formats
JSON
Excel
Getting Started

1. appsettings.json 설정

DatasheetGenerator/DatasheetGenerator/appsettings.example.json 을 복사하여 같은 경로에 appsettings.json 으로 저장한 후, OutputRootPath 를 실제 출력 경로로 수정합니다.

2. Handsontable 파일 준비

라이선스 문제로 Handsontable 파일은 레포지토리에 포함되어 있지 않습니다.
Handsontable 공식 사이트(https://handsontable.com)에서 다운로드한 후 아래 경로에 배치합니다.

DatasheetGenerator/DatasheetGenerator/wwwroot/handsontable.full.min.js
DatasheetGenerator/DatasheetGenerator/wwwroot/handsontable.full.min.css

3. 빌드

dotnet build DatasheetGenerator/DatasheetGenerator.slnx

Generated With

This project was generated with Claude Code.

Repository

This repository contains the source code for DatasheetGenerator.