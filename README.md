# TemplateProcessor

TemplateProcessor - учебный проект на .NET 8 для практики чистой архитектуры, работы с шаблонами документов, OpenXML/ClosedXML, LaTeX, тестирования, обработки ошибок, логирования и базовых защитных проверок.

Проект умеет анализировать шаблоны, находить обязательные переменные и генерировать документы из данных. Поддерживаются шаблоны Word (`.docx`), Excel (`.xlsx`) и LaTeX (`.tex`).

## Что умеет проект

- Находит переменные в шаблоне до генерации документа.
- Подставляет скалярные значения: `{{ClientName}}`, `{{Date}}`, `{{TotalSum}}`.
- Обрабатывает коллекции: `{{#Items}} ... {{/Items}}`.
- Генерирует документы в форматах `Docx`, `Xlsx`, `Tex`.
- Поддерживает PDF для Word и LaTeX через внешние инструменты.
- Сохраняет объединенные ячейки в Word и Excel при клонировании строк коллекций.
- Экранирует спецсимволы LaTeX при подстановке значений.
- Защищает чтение локальных файлов от path traversal.
- Логирует ключевые операции чтения шаблонов.
- Покрыт unit, integration и performance тестами.

## Поддерживаемые форматы

| Входной шаблон | Анализатор | Рендерер | Выходные форматы |
| --- | --- | --- | --- |
| `.docx` | `WordTemplateAnalyzer` | `WordRenderer` | `Docx`, `Pdf` |
| `.xlsx` | `ExcelTemplateAnalyzer` | `ExcelRenderer` | `Xlsx` |
| `.tex` | `LatexTemplateAnalyzer` | `LatexRenderer` | `Tex`, `Pdf` |

PDF доступен только при установленных внешних инструментах. Генерация `Docx`, `Xlsx` и `Tex` не требует LibreOffice или LaTeX-дистрибутива.

## Архитектура

Решение разделено на четыре проекта:

- `TemplateProcessor.Domain`  
  Доменные модели, порты, исключения и сервис валидации.

- `TemplateProcessor.Application`  
  Use cases и фасад `TemplateEngineModule`.

- `TemplateProcessor.Infrastructure`  
  Реализации анализаторов, рендереров, конвертеров и локального хранилища файлов.

- `TemplateProcessor.Tests`  
  Unit, integration, infrastructure и performance тесты.

Основной сценарий работы:

1. `LocalFileStorage` читает шаблон из файла.
2. Анализатор конкретного формата находит переменные шаблона.
3. `TemplateValidationService` проверяет, что в `TemplateContext` есть все обязательные данные.
4. Рендерер конкретного формата генерирует итоговый документ.

## Требования

Обязательное:

- .NET 8 SDK

Опционально для PDF:

- LibreOffice для конвертации Word в PDF.
- TeX Live или MiKTeX для конвертации LaTeX в PDF.

Проверка окружения:

```powershell
dotnet --version
soffice --version
pdflatex --version
```

Если `soffice` или `pdflatex` не установлены, соответствующая PDF-конвертация будет недоступна. Остальные форматы продолжат работать.

## Сборка

Из корня репозитория:

```powershell
dotnet restore .\TemplateProcessor.sln
dotnet build .\TemplateProcessor.sln
```

## Запуск тестов

Все тесты:

```powershell
dotnet test .\TemplateProcessor.Tests\TemplateProcessor.Tests.csproj
```

Только интеграционные тесты:

```powershell
dotnet test .\TemplateProcessor.Tests\TemplateProcessor.Tests.csproj --filter "FullyQualifiedName~Integration"
```

Только performance test:

```powershell
dotnet test .\TemplateProcessor.Tests\TemplateProcessor.Tests.csproj --filter "FullyQualifiedName~RendererPerformanceTests"
```

Performance test генерирует LaTeX-документ размером от 5 до 10 МБ и проверяет, что рендеринг укладывается в 5 секунд.

## Синтаксис шаблонов

Скалярные переменные:

```text
Договор: {{ContractNumber}}
Клиент: {{ClientName}}
Дата: {{Date}}
```

Коллекции:

```text
{{#Items}}
{{Name}} {{Quantity}} {{Price}} {{Total}}
{{/Items}}
```

Для Word и Excel коллекции обычно размещаются в таблице. Рендерер удаляет строки-маркеры `{{#Items}}` и `{{/Items}}`, а строки между ними клонирует для каждого элемента коллекции.

Для LaTeX коллекция может оборачивать любой повторяющийся текстовый блок.

## Пример использования

Пример ниже показывает сборку фасада для Word-шаблона. Для Excel и LaTeX нужно использовать соответствующую пару анализатор/рендерер.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TemplateProcessor.Application.Abstractions;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure;

var templatesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Templates");

var services = new ServiceCollection();
services.AddTemplateProcessor(templatesPath);

await using var provider = services.BuildServiceProvider();
var module = provider.GetRequiredService<ITemplateEngineModule>();

var context = new TemplateContextDto
{
    Scalars = new Dictionary<string, object>
    {
        ["ContractNumber"] = "123-456",
        ["ClientName"] = "Demo Client",
        ["Date"] = "01.01.2024",
        ["TotalSum"] = 15000.50
    },
    Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>
    {
        ["Items"] = new List<Dictionary<string, object>>
        {
            new() { ["Name"] = "Item A", ["Quantity"] = 2, ["Price"] = 1000.00, ["Total"] = 2000.00 },
            new() { ["Name"] = "Item B", ["Quantity"] = 1, ["Price"] = 5000.00, ["Total"] = 5000.00 }
        }
    }
};

var variables = await module.GetRequiredVariablesAsync("SampleTemplate.docx");

await using var result = await module.RenderDocumentAsync(
    "SampleTemplate.docx",
    OutputFormat.Docx,
    context);

await using var file = File.Create("GeneratedDocument.docx");
await result.CopyToAsync(file);
```

Для Excel:

```csharp
var outputFormat = OutputFormat.Xlsx;
```

Для LaTeX:

```csharp
var outputFormat = OutputFormat.Tex;
```

## Безопасность чтения файлов

`LocalFileStorage` можно создать с базовой директорией:

```csharp
var storage = new LocalFileStorage(@"C:\Templates");
```

В этом режиме:

- относительные пути считаются относительно базовой директории;
- абсолютные пути тоже должны находиться внутри базовой директории;
- попытки выйти наружу через `..\secret.docx` блокируются;
- пути с похожим префиксом, например `C:\Templates2`, тоже блокируются.

Можно передать логгер:

```csharp
var storage = new LocalFileStorage(@"C:\Templates", logger);
```

Если логгер не передан, используется `NullLogger`.

## PDF-конвертация

Word в PDF конвертируется через LibreOffice:

```powershell
soffice --headless --convert-to pdf ...
```

LaTeX в PDF конвертируется через `pdflatex`:

```powershell
pdflatex -interaction=nonstopmode ...
```

Конвертеры работают через временные файлы и удаляют их после завершения. Если внешний инструмент не установлен или завершился с ошибкой, будет выброшено исключение с описанием проблемы.

## Тестовые шаблоны

Интеграционные шаблоны лежат здесь:

```text
TemplateProcessor.Tests/Fixtures/Templates
```

При сборке тестового проекта они копируются в выходную директорию.

## Заметки

- Фасад сейчас собирается под конкретный формат: анализатор и рендерер должны соответствовать типу шаблона.
- Перед рендерингом проверяются все обязательные переменные.
- LaTeX-значения экранируют символы `#`, `$`, `%`, `&`, `_`, `{`, `}`, `\`, `~`, `^`.
- Word и Excel рендереры сохраняют объединенные ячейки при клонировании строк коллекций.
