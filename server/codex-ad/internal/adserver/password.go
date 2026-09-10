package adserver

import (
	"bufio"
	"errors"
	"fmt"
	"io"
	"strings"

	"golang.org/x/crypto/bcrypt"
)

func HashPassword(input io.Reader, output io.Writer) error {
	password, err := bufio.NewReader(io.LimitReader(input, 1024)).ReadString('\n')
	if err != nil && !errors.Is(err, io.EOF) {
		return err
	}
	password = strings.TrimSpace(password)
	if len(password) < 20 {
		return errors.New("admin password must contain at least 20 characters")
	}
	hash, err := bcrypt.GenerateFromPassword([]byte(password), 12)
	if err != nil {
		return err
	}
	_, err = fmt.Fprintln(output, string(hash))
	return err
}
