# Test EventService query infrastructure via racing leaderboard endpoint
# This validates that the QueryAsync<T> method works correctly

GET http://127.0.0.1:8000/game/racing/leaderboard?track_id=gocart_track&metric=best_lap&limit=10
HTTP 200
[Asserts]
jsonpath "$.track_id" == "gocart_track"
jsonpath "$.metric" == "best_lap"
jsonpath "$.leaderboard" isCollection
