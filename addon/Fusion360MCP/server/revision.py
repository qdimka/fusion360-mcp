"""Process-local revision and reported-name state for Fusion documents."""

from collections import defaultdict


class RevisionState:
    def __init__(self):
        self._revisions = defaultdict(int)
        self._reported = defaultdict(set)

    def current(self, identity):
        return self._revisions[identity]

    def increment(self, identity):
        self._revisions[identity] += 1
        return self._revisions[identity]

    def record_names(self, identity, names):
        self._reported[identity].update(name for name in names if name)

    def was_reported(self, identity, name):
        return name in self._reported[identity]


state = RevisionState()
