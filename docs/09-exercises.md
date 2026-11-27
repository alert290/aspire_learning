# Урок 9. Упражнения и что изучать дальше

Задания от простых к сложным. Каждое закрепляет конкретную возможность Aspire.

## 🟢 Разминка

1. **Новый параметр.** Добавь параметр `menu-cache-seconds` (по умолчанию 60) и передай его в MenuApi
   вместо захардкоженного TTL кэша. *(Урок 5)*
2. **Своя метрика.** Счётчик `coffeeshop.menu.cache_requests` с тегом `result` = `hit` / `miss`. Найди его
   в Metrics. *(Урок 4)*
3. **Своя команда.** Команда `WithCommand` (не HTTP!) у ресурса `cache`, которая выводит в результат число
   ключей в Redis. Подсказка: колбэк получает `ExecuteCommandContext`, а строку подключения можно получить
   через `await cache.Resource.ConnectionStringExpression.GetValueAsync(ct)`. *(Урок 4)*

## 🟡 Посерьёзнее

4. **Отмена заказа.** `POST /orders/{id}/cancel` в OrdersApi: можно, пока статус `Pending`. Бариста должен
   пропустить отменённый заказ. Как он об этом узнает? (Варианты: проверить статус по HTTP у OrdersApi,
   или отдельное сообщение в RabbitMQ.) *(Уроки 1, 3)*
5. **Сервис уведомлений.** Новый Worker `NotificationsWorker`, который слушает «заказ готов» и пишет в лог
   «📣 Анна, ваш латте готов!». Для этого бариста должен публиковать событие в **exchange типа fanout**,
   чтобы его получили и OrdersApi, и уведомления. Добавь сервис в AppHost и проверь, что он виден в трейсе. *(Уроки 1, 3, 4)*
6. **Почтовый сервер.** Добавь контейнер [Mailpit](https://mailpit.axllent.org/) через `AddContainer` и отправляй
   «письмо» при готовности заказа. Ссылка на веб-интерфейс Mailpit должна быть в дашборде. *(Урок 6)*
7. **Тест на отмену.** End-to-end тест для задания 4. *(Урок 7)*

## 🔴 Для смелых

8. **API Gateway на YARP.** Пакет `Aspire.Hosting.Yarp`: `builder.AddYarp("gateway")` с маршрутами
   `/api/menu/**` → `menuapi`, `/api/orders/**` → `ordersapi`. Переключи Web на работу через шлюз.
9. **Авторизация через Keycloak.** `Aspire.Hosting.Keycloak` в AppHost и `Aspire.Keycloak.Authentication` в
   сервисах: вход в Blazor и защита админских эндпоинтов. Не забудь, что HTTP-команды дашборда тогда
   должны передавать токен (`HttpCommandOptions.PrepareRequest`).
10. **Сервис на другом языке.** Python-сервис рекомендаций (`Aspire.Hosting.Python`, `AddPythonApp` /
    `AddUvicornApp`) или Node.js (`Aspire.Hosting.JavaScript`). Он может читать `MENUDB_URI` из окружения:
    именно для этого Aspire передаёт свойства подключения отдельными переменными.
11. **Transactional Outbox.** Сейчас между `SaveChanges` и публикацией в RabbitMQ есть «окно»
    (см. комментарий в `OrderService.cs`). Реализуй outbox: событие пишется в ту же транзакцию, а фоновый
    сервис публикует его.
12. **Деплой в облако.** Замени Compose на Azure Container Apps (урок 8) и разверни систему через `aspire deploy`.

## Куда смотреть дальше

- Документация прямо из терминала: `aspire docs search "<запрос>"`, `aspire docs get <slug>`.
- Официальный сайт: https://aspire.dev
- Каталог интеграций: `aspire integration` (CLI) или раздел Integrations на сайте.
- Исходники Aspire и примеры: https://github.com/microsoft/aspire

Удачи и вкусного кофе! ☕
