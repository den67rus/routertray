[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$listingPath = Join-Path $repositoryRoot 'docs\store-assets\partner-center-upload\listingData.csv'
$rows = @(Import-Csv -LiteralPath $listingPath)
$columns = @($rows[0].PSObject.Properties.Name | Where-Object { $_ -ne 'en' })
$languages = @($columns | Select-Object -Skip 4)
$releaseNotes = @{
    'en-us' = 'First Microsoft Store release. Includes guided router setup, multiple profiles, automatic network matching, password or token sign-in, and updates through Microsoft Store.'
    da = 'Første udgave i Microsoft Store. Med guidet routeropsætning, flere profiler, automatisk netværksvalg, login med adgangskode eller token og opdateringer via Microsoft Store.'
    de = 'Erste Veröffentlichung im Microsoft Store. Mit geführter Routereinrichtung, mehreren Profilen, automatischer Netzwerkzuordnung, Anmeldung per Passwort oder Token und Updates über den Microsoft Store.'
    es = 'Primera versión en Microsoft Store. Incluye configuración guiada del router, varios perfiles, selección automática según la red, acceso con contraseña o token y actualizaciones mediante Microsoft Store.'
    fi = 'Ensimmäinen Microsoft Store -julkaisu. Mukana ohjattu reitittimen käyttöönotto, useita profiileja, automaattinen verkon tunnistus, kirjautuminen salasanalla tai tunnuksella ja Microsoft Store -päivitykset.'
    fr = 'Première version sur le Microsoft Store. Configuration guidée du routeur, plusieurs profils, sélection automatique selon le réseau, connexion par mot de passe ou jeton et mises à jour via le Microsoft Store.'
    it = 'Prima versione nel Microsoft Store. Configurazione guidata del router, più profili, selezione automatica in base alla rete, accesso con password o token e aggiornamenti tramite Microsoft Store.'
    pl = 'Pierwsze wydanie w Microsoft Store. Kreator konfiguracji routera, wiele profili, automatyczny wybór według sieci, logowanie hasłem lub tokenem i aktualizacje przez Microsoft Store.'
    pt = 'Primeira versão na Microsoft Store. Configuração guiada do router, vários perfis, seleção automática pela rede, autenticação com palavra-passe ou token e atualizações pela Microsoft Store.'
    ru = 'Первый выпуск в Microsoft Store. Пошаговая настройка роутера, несколько профилей, автоматический выбор по сети Windows, вход по паролю или токену и обновления через Microsoft Store.'
    sv = 'Första versionen i Microsoft Store. Guidad routerkonfiguration, flera profiler, automatiskt profilval efter nätverk, inloggning med lösenord eller token och uppdateringar via Microsoft Store.'
    tr = 'İlk Microsoft Store sürümü. Adım adım yönlendirici kurulumu, birden fazla profil, ağa göre otomatik seçim, parola veya belirteç ile oturum açma ve Microsoft Store üzerinden güncellemeler.'
    uk = 'Перший випуск у Microsoft Store. Покрокове налаштування роутера, кілька профілів, автоматичний вибір за мережею Windows, вхід за паролем або токеном та оновлення через Microsoft Store.'
}
$routerTerms = @{
    'en-us' = @('router', 'network profiles'); da = @('router', 'netværksprofiler')
    de = @('Router', 'Netzwerkprofile'); es = @('router', 'perfiles de red')
    fi = @('reititin', 'verkkoprofiilit'); fr = @('routeur', 'profils réseau')
    it = @('router', 'profili di rete'); pl = @('router', 'profile sieciowe')
    pt = @('router', 'perfis de rede'); ru = @('роутер', 'профили сети')
    sv = @('router', 'nätverksprofiler'); tr = @('yönlendirici', 'ağ profilleri')
    uk = @('роутер', 'профілі мережі')
}

foreach ($row in $rows) {
    foreach ($language in $languages) {
        $row.$language = $row.$language.Replace('\n', "`n").Trim()
        switch ($row.Field) {
            'ReleaseNotes' { $row.$language = $releaseNotes[$language] }
            'DevStudio' { $row.$language = 'den67rus' }
            'SearchTerm3' { $row.$language = $routerTerms[$language][0] }
            'SearchTerm4' { $row.$language = $routerTerms[$language][1] }
            'CopyrightTrademarkInformation' {
                $row.$language = 'Copyright (c) 2026 Denis Sevostyanov. RouterTray is an independent application. Keenetic and Netcraze product names belong to their respective owners; no affiliation or endorsement is implied.'
            }
            'AdditionalLicenseTerms' {
                $row.$language = 'RouterTray is open-source software distributed under the MIT license. License: https://github.com/den67rus/routertray/blob/main/LICENSE'
            }
        }
    }
}

$description = $rows | Where-Object Field -eq 'Description'
$description.ru = $description.ru.Replace('сценарий например,', 'сценарий — например,').Replace(
    'семейный профиль без постоянного', 'семейный профиль — без постоянного').Replace(
    'один раз и нужный', 'один раз — и нужный')

$rows | Select-Object -Property $columns |
    Export-Csv -LiteralPath $listingPath -NoTypeInformation -Encoding utf8BOM

# Keep the text-only alternative without creating image-path cells.
$textRows = $rows | Where-Object { $_.'Type (Тип)' -eq 'Текст' }
$textRows | Select-Object -Property $columns |
    Export-Csv -LiteralPath (Join-Path $repositoryRoot 'docs\store-assets\listingData-no-image.csv') `
        -NoTypeInformation -Encoding utf8BOM
Write-Host "Prepared $($languages.Count) Store listing languages with UTF-8 and real line breaks."
