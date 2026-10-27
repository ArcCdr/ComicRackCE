# Test scripts for the P4.3 acceptance. Two script lists whose keys are the ones that the fixture database's smart lists name
# ("Missing (to fix)" and "Next issues to read"); the bodies are small stand-ins written for the tests, not the scripts of any library.
#
#@Name Find missing issues
#@Key findMissingIssues
#@Hook CreateBookList
#@PCount 0
def FindMissingIssues(books, a, b):
	# the books that come right after a gap in the numbers of their series
	numbers = {}
	for book in books:
		n = book.ShadowNumber
		if n != "" and n.isdigit():
			numbers.setdefault(book.ShadowSeries, []).append((int(n), book))
	result = []
	for series in numbers:
		items = sorted(numbers[series], key=lambda item: item[0])
		for i in range(1, len(items)):
			if items[i][0] > items[i - 1][0] + 1:
				result.append(items[i][1])
	return result

#@Name Next issues to read
#@Key NextIssuesToRead
#@Hook CreateBookList
#@PCount 0
def NextIssuesToRead(books, a, b):
	seen = set()
	result = []
	for book in books:
		if book.OpenedTime == book.OpenedTime.MinValue and book.ShadowSeries not in seen:
			seen.add(book.ShadowSeries)
			result.append(book)
	return result
