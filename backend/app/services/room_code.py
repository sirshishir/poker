import secrets

# Exclude visually ambiguous characters: O/0, I/1, S/5, Z/2
_SAFE_CHARS = "ABCDEFGHJKLMNPQRTUVWXY346789"


def generate_room_code(length: int = 6) -> str:
    return "".join(secrets.choice(_SAFE_CHARS) for _ in range(length))
