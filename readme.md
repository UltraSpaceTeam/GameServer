# Game server
Это отвечает за направление игроков к их ServerInstance
1. Auth (Авторизация)
Register

Регистрация нового пользователя.

    URL: /auth/register

    Method: POST

    Auth: None

Request:
JSON

{
  "username": "user1",
  "password": "password123"
}

Response (200 OK):
JSON

{
  "token": "eyJhbGci...",
  "player_id": 1,
  "username": "user1"
}

Errors: 400 (Bad Data), 401 (Username taken).
Login

Вход и получение токена.

    URL: /auth/login

    Method: POST

    Auth: None

Request:
JSON

{
  "username": "user1",
  "password": "password123"
}

Response (200 OK): Аналогично Register. Errors: 401 (Invalid credentials).
2. Leaderboard (Статистика)
Get Top Players

Получение топа игроков.

    URL: /leaderboard

    Method: GET

    Auth: Bearer

    Query Params:

        players_limit (int, default: 10)

        player_id (int, required)

Response (200 OK):
JSON

{
  "leaderboard": [
    {
      "nickname": "Player1",
      "kills": 100,
      "deaths": 50,
      "gamesPlayed": 10
    }
  ],
  "totalPlayers": 150
}

Get Player Stat

Статистика конкретного игрока.

    URL: /leaderboard/{player_id}

    Method: GET

    Auth: Bearer

    Path Params: player_id (int)

Response (200 OK):
JSON

{
  "nickname": "Player1",
  "kills": 100,
  "deaths": 50,
  "gamesPlayed": 10
}

Errors: 404 (Not Found).
3. Matchmaking (Client)
Join Game

Поиск доступной сессии (State: Waiting, Slots available).

    URL: /games/join

    Method: GET

    Auth: Bearer

    Query Params: player_id (int)

Response (200 OK):
JSON

{
  "ip": "127.0.0.1",
  "port": 7777,
  "key": "session_secret_key"
}

Errors: 404 (No active sessions).
4. Session Instance (Server-to-Server)
Register Session

Инициализация Unity-сервера.

    URL: /games/register

    Method: POST

    Auth: None (Whitelist IP recommended)

Request:
JSON

{
  "port": 7777,
  "ipAddress": "10.0.0.1",
  "maxPlayers": 20
}

Response (200 OK):
JSON

{
  "sessionId": 123,
  "key": "session_secret_key"
}

Player Joined

Фиксация подключения игрока к серверу.

    URL: /games/player_joined

    Method: POST

Request:
JSON

{
  "sessionId": 123,
  "playerId": 1
}

Healthcheck

Обновление статуса и списка игроков. Тайм-аут удаления сессии: 60 сек.

    URL: /games/healthcheck

    Method: POST

Request:
JSON

{
  "sessionId": 123,
  "state": "Playing",
  "time": "12:00:00",
  "players": [1, 5, 8]
}

Submit Result

Сохранение результатов матча и закрытие сессии. Атомарная транзакция.

    URL: /games/result

    Method: POST

Request:
JSON

{
  "sessionId": 123,
  "leaderboard": [
    { "playerId": 1, "kills": 5, "deaths": 1 },
    { "playerId": 5, "kills": 0, "deaths": 5 }
  ]
}
